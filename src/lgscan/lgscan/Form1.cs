﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.OleDb;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;

using OfficeOpenXml;
using OfficeOpenXml.Style;

using BarCodeScan;

namespace lgscan {
    public partial class Form1 : Form {
        private string strCurrentBoxNo = "";
        private string strCurrentBarCode = "";
        private int iTotalNumber;
        private int iRow = 1;
        private ExcelPackage package;
        private string strFilePath;
        private int iLastStatus = -1;
        private bool bBusy;
        private int iCurrentIndex;

        private BarCodeReader codereader;
        private bool isCameraReading;

        private bool runInspecting = true;

        private const string PLC_SLOT_Y0 = "Y0";
        private const string PLC_SLOT_Y3 = "Y3";

        const string DATE_FMT = "yyyy-MM-dd HH:mm:ss";

        public static lgscan.Conf conf = null;
        public static void loadConf() {
            var path = Path.Combine(Application.StartupPath, "config.hjson");
            conf = lgscan.Conf.loadFile(path);
        }

        private object LOCK_PLC = new object();

        public Form1() {
            InitializeComponent();

            Text = "装柜计数系统 v1.1";
            lblResult.Text = ""; // 清除报警信息。    
        }

        private void Form1_Load(object sender, EventArgs e) {
            Form1.loadConf();
            writeLog(conf.ToString());

            // PLC.setHandler(plcListener);
            PLC.Open(conf.plc.port, conf.plc.baudrate);

            runInspecting = true;
            startPlcInspection();
        }

        private void btnExit_Click(object sender, EventArgs e) {
            var b = MessageBox.Show("确认要退出软件吗?", "退出确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (b == DialogResult.Yes) {
                Close();
            }
        }

        private void showLineInfo(int status) {
            lock (LOCK_PLC) {
                Invoke((MethodInvoker)delegate {
                    switch (status) {
                        case 1:
                            lblResult.Text = "当前商品已经装满，请装其它商品！";
                            PLC.setM("Y6", 1);
                            break;
                        case 2:
                            lblResult.Text = "当前商品未装满，不允许装其它商品！";
                            PLC.setM("Y7", 1);
                            break;
                        case 3:
                            lblResult.Text = "重量不合格！";
                            PLC.setM("Y4", 1);
                            break;
                        case -1:
                            lblResult.Text = "号码异常！";
                            PLC.setM("Y5", 1);
                            break;
                        default:
                            lblResult.Text = "";
                            PLC.setM("Y5", 0);
                            PLC.setM("Y6", 0);
                            PLC.setM("Y7", 0);
                            PLC.setM("Y4", 0);
                            break;
                    }
                });
            }
        }

        /// <summary>
        /// 处理相机读到的号码。
        /// </summary>
        /// <param name="barcode"></param>
        private bool HandleBarCode(string barcode) {
            var num = InputItem(barcode);

            if (iLastStatus != num) {
                try {
                    iLastStatus = num;
                    lock (LOCK_PLC) {
                        PLC.setM("R0", (num == 0) ? 1 : 0);
                    }
                    WriteFile(lblBarCode.Text + iLastStatus.ToString());
                } catch (Exception ex) {
                    WriteFile("HandleBarCode启动PLC出错！错误为：" + ex.Message);
                }
            }

            showLineInfo(num);
            return num == 0;
        }

        private bool HandleWeight(string code, double weight) {
            var rt = false;
            Invoke((MethodInvoker)delegate {
                for (var i = 0; i < gvData.Rows.Count; i++) {
                    var row = gvData.Rows[i];
                    var evno = row.Cells["EvNo"].Value.ToString();
                    var gw = (double)row.Cells["GrossWt"].Value;
                    var wt = (double)row.Cells["WtTolerance"].Value;

                    if (evno == code && Math.Abs(gw - weight) <= wt) {
                        rt = true;
                        break;
                    }
                }
            });
            return rt;
        }

        public static DataSet ToDataTable(string filePath) {
            var extension = Path.GetExtension(filePath);
            if (string.IsNullOrEmpty(extension)) {
                return null;
            }
            string connectionString;
            if (extension == ".xls") {
                connectionString = "Provider=Microsoft.Jet.OLEDB.4.0;Data Source=\"" + filePath + "\";Extended Properties=\"Excel 8.0;HDR=YES;IMEX=1\"";
            } else {
                connectionString = "Provider=Microsoft.ACE.OLEDB.12.0;Data Source=\"" + filePath + "\";Extended Properties=\"Excel 12.0;HDR=YES;IMEX=1\"";
            }
            var format = "Select * FROM [{0}]";
            OleDbConnection oleDbConnection = null;
            OleDbDataAdapter oleDbDataAdapter = null;
            var dataSet = new DataSet();
            try {
                oleDbConnection = new OleDbConnection(connectionString);
                oleDbConnection.Open();
                var oleDbSchemaTable = oleDbConnection.GetOleDbSchemaTable(OleDbSchemaGuid.Tables, new object[]
                {
                    null,
                    null,
                    null,
                    "TABLE"
                });
                oleDbDataAdapter = new OleDbDataAdapter();
                for (int i = 0; i < oleDbSchemaTable.Rows.Count; i++) {
                    var text = (string)oleDbSchemaTable.Rows[i]["TABLE_NAME"];
                    if (!text.Contains("$") || text.Replace("'", "").EndsWith("$")) {
                        oleDbDataAdapter.SelectCommand = new OleDbCommand(string.Format(format, text), oleDbConnection);
                        var dataSet2 = new DataSet();
                        oleDbDataAdapter.Fill(dataSet2, text);
                        dataSet.Tables.Add(dataSet2.Tables[0].Copy());
                    }
                }
            } catch (Exception ex) {
                if (ex.Message.IndexOf("未在本地计算机上注册") >= 0) {
                    MessageBox.Show(ex.Message);
                } else {
                    MessageBox.Show("请关闭文件后再点击打开！" + ex.Message);
                }
            } finally {
                if (oleDbConnection.State == ConnectionState.Open) {
                    oleDbConnection.Close();
                    oleDbDataAdapter.Dispose();
                    oleDbConnection.Dispose();
                }
            }
            return dataSet;
        }

        private void openExcel() {
            var text = "";
            var num = 0;
            openFile.Filter = "Excel Files|*.xls;*.xlsx";
            if (openFile.ShowDialog() == DialogResult.OK) {
                text = openFile.FileName;
            }
            if (text == "") {
                return;
            }
            strFilePath = text;
            iTotalNumber = 0;
            try {
                var dataSet = ToDataTable(text);
                if (dataSet.Tables.Count != 0) {
                    var dataTable = dataSet.Tables[0];
                    var dataTable2 = new DataTable();
                    if (dataTable != null) {
                        lblBoxNo.Text = dataTable.Rows[2][14].ToString();
                        strCurrentBoxNo = lblBoxNo.Text;
                        dataTable2.Columns.Add("EVNo");
                        dataTable2.Columns.Add("Number", typeof(int));
                        dataTable2.Columns.Add("Already", typeof(int));
                        dataTable2.Columns.Add("GrossWt", typeof(decimal));
                        dataTable2.Columns.Add("WtTolerance", typeof(decimal));
                        var fileName = GetFileName();
                        if (File.Exists(fileName)) {
                            if (MessageBox.Show("当前柜已有装柜记录，是否继续之前的装柜？", "重要提示", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) == DialogResult.Yes) {
                                var dataSet2 = ToDataTable(fileName);
                                if (dataSet2.Tables.Count == 0) {
                                    MessageBox.Show("此文件数据已经被修改，无法正确加载！请删除\r\n" + fileName + " 后再打开！", "重要提示", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                                    return;
                                }
                                var dataTable3 = dataSet2.Tables[lblBoxNo.Text + "$"];
                                var num2 = 0;
                                var num3 = 0;
                                var text2 = "";
                                for (int i = 0; i < dataTable3.Rows.Count; i++) {
                                    var dataRow = dataTable3.Rows[i];
                                    if (dataRow[0].ToString() == "") {
                                        break;
                                    }
                                    var num4 = int.Parse(dataRow[1].ToString());
                                    var num5 = int.Parse(dataRow[2].ToString());
                                    var dataRow2 = dataTable2.NewRow();
                                    dataRow2["EVNo"] = dataRow[0].ToString();
                                    dataRow2["Number"] = num4;
                                    dataRow2["Already"] = num5;
                                    dataRow2["GrossWt"] = dataRow[3];
                                    dataRow2["WtTolerance"] = dataRow[4];
                                    if (num4 > num5 & num5 > 0 & num3 == 0) {
                                        num2 = num4;
                                        num3 = num5;
                                        text2 = dataRow[0].ToString();
                                        iCurrentIndex = i;
                                    }
                                    num += int.Parse(dataRow[2].ToString());
                                    dataTable2.Rows.Add(dataRow2);
                                }
                                lblBarCode.Text = text2;
                                lblCurrentNeedNumber.Text = num2.ToString();
                                lblCurrentNumber.Text = num3.ToString();
                                lblAlreadyNumber.Text = num.ToString();
                            } else {
                                System.IO.File.Delete(fileName);
                                lblBarCode.Text = "";
                                lblCurrentNeedNumber.Text = "0";
                                lblCurrentNumber.Text = "0";
                                lblAlreadyNumber.Text = "0";
                                for (int j = 6; j < dataTable.Rows.Count; j++) {
                                    var dataRow = dataTable.Rows[j];
                                    if (dataRow[0].ToString() == "") {
                                        break;
                                    }
                                    var dataRow2 = dataTable2.NewRow();
                                    dataRow2["EVNo"] = dataRow[2].ToString();
                                    dataRow2["Number"] = int.Parse(dataRow[7].ToString());
                                    dataRow2["Already"] = 0;
                                    dataRow2["GrossWt"] = decimal.Parse(dataRow[18].ToString());
                                    dataRow2["WtTolerance"] = decimal.Parse(dataRow[19].ToString());
                                    dataTable2.Rows.Add(dataRow2);
                                }
                            }
                        } else {
                            for (int k = 6; k < dataTable.Rows.Count; k++) {
                                var dataRow = dataTable.Rows[k];
                                if (dataRow[2].ToString() == "") {
                                    break;
                                }
                                var dataRow2 = dataTable2.NewRow();
                                dataRow2["EVNo"] = dataRow[2].ToString();
                                dataRow2["Number"] = int.Parse(dataRow[7].ToString());
                                dataRow2["Already"] = 0;
                                dataRow2["GrossWt"] = decimal.Parse(dataRow[18].ToString());
                                dataRow2["WtTolerance"] = decimal.Parse(dataRow[19].ToString());
                                dataTable2.Rows.Add(dataRow2);
                            }
                        }
                        gvData.DataSource = dataTable2;
                        gvData.Columns[0].HeaderCell.Value = "EV号码";
                        gvData.Columns[1].HeaderCell.Value = "箱数";
                        gvData.Columns[2].HeaderCell.Value = "已装箱数";
                        gvData.Columns[3].HeaderCell.Value = "毛重(kg)";
                        gvData.Columns[4].HeaderCell.Value = "毛重公差(kg)";
                        gvData.Columns[0].Width = 125;
                        gvData.Columns[1].Width = 90;
                        gvData.Columns[2].Width = 90;
                        gvData.Columns[3].Width = 90;
                        gvData.Columns[4].Width = 130;
                        gvData.Columns[1].DefaultCellStyle.Alignment = DataGridViewContentAlignment.BottomRight;
                        gvData.Columns[2].DefaultCellStyle.Alignment = DataGridViewContentAlignment.BottomRight;
                        gvData.Columns[3].DefaultCellStyle.Alignment = DataGridViewContentAlignment.BottomRight;
                        gvData.Columns[3].DefaultCellStyle.Format = "f4";
                        gvData.Columns[4].DefaultCellStyle.Alignment = DataGridViewContentAlignment.BottomRight;
                        gvData.Columns[4].DefaultCellStyle.Format = "f6";
                        if (gvData.Rows.Count > 0 & num > 0) {
                            gvData.Rows[iCurrentIndex].Selected = true;
                        }

                        lblResult.Text = "";

                        lock (LOCK_PLC) {
                            PLC.setM("Y5", 0);
                            PLC.setM("Y6", 0);
                            PLC.setM("Y7", 0);
                        }

                        writeLog("加载Excel完毕。");
                    }
                }
            } catch (Exception ex2) {
                WriteFile("加载Excel数据出错，请检查Excel格式是否正确！" + ex2.Message);
                MessageBox.Show("加载Excel数据出错，请检查Excel格式是否正确！", "错误提示", MessageBoxButtons.OK, MessageBoxIcon.Hand);
            }
        }

        private void gvData_RowPostPaint(object sender, DataGridViewRowPostPaintEventArgs e) {
            if (e.RowIndex == gvData.Rows.Count - 1) {
                return;
            }
            var bounds = new Rectangle(e.RowBounds.Location.X, e.RowBounds.Location.Y, gvData.RowHeadersWidth - 4, e.RowBounds.Height);
            TextRenderer.DrawText(e.Graphics, (e.RowIndex + 1).ToString(), gvData.RowHeadersDefaultCellStyle.Font, bounds, gvData.RowHeadersDefaultCellStyle.ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        /// <summary>
        /// 装柜
        /// </summary>
        /// <param name="code">扫描的码</param>
        /// <returns>-1表示有异常；0表示正常装柜；1表示装满了；2表示码不在装柜单里；</returns>
        public int InputItem(string code) {
            strCurrentBarCode = code;
            var result = -1;
            Invoke((MethodInvoker)delegate {
                try {
                    for (int i = 0; i < gvData.Rows.Count - 1; i++) {
                        var a = gvData.Rows[i].Cells[0].Value.ToString();
                        var num = int.Parse(gvData.Rows[iCurrentIndex].Cells[1].Value.ToString());
                        var num2 = int.Parse(gvData.Rows[iCurrentIndex].Cells[2].Value.ToString());
                        if (lblBarCode.Text != strCurrentBarCode & num > num2 & num2 > 0) {
                            result = 2;
                            break;
                        }
                        gvData.Rows[i].Selected = false;
                        int num3;
                        int num4;
                        if (a == strCurrentBarCode) {
                            num3 = int.Parse(gvData.Rows[i].Cells[1].Value.ToString());
                            num4 = int.Parse(gvData.Rows[i].Cells[2].Value.ToString());
                            lblCurrentNeedNumber.Text = num3.ToString();
                            lblBarCode.Text = strCurrentBarCode;
                            if (num3 > num4) {
                                gvData.Rows[i].Cells[2].Value = int.Parse(gvData.Rows[i].Cells[2].Value.ToString()) + 1;
                                lblCurrentNumber.Text = gvData.Rows[i].Cells[2].Value.ToString();
                                result = 0;
                                iRow++;
                                SaveAlreadyInfo();
                                SaveAlreadyItemTimeInfo(lblBarCode.Text);
                            } else {
                                lblCurrentNumber.Text = num4.ToString();
                                result = 1;
                            }
                            iCurrentIndex = i;
                            gvData.Rows[i].Selected = true;
                            break;
                        }
                        num3 = int.Parse(gvData.Rows[iCurrentIndex].Cells[1].Value.ToString());
                        num4 = int.Parse(gvData.Rows[iCurrentIndex].Cells[2].Value.ToString());
                        if (strCurrentBarCode != gvData.Rows[iCurrentIndex].Cells[0].Value.ToString()) {
                            if (num3 > num4 & num4 > 0) {
                                if (strCurrentBarCode == gvData.Rows[i].Cells[0].Value.ToString()) {
                                    gvData.Rows[i].Selected = true;
                                }
                                result = 2;
                                break;
                            }
                            if (num4 == 0) {
                                iRow = 0;
                            }
                        }
                    }
                    iTotalNumber = 0;
                    for (int j = 0; j < gvData.Rows.Count - 1; j++) {
                        iTotalNumber += int.Parse(gvData.Rows[j].Cells[2].Value.ToString());
                    }
                    lblAlreadyNumber.Text = iTotalNumber.ToString();
                } catch (Exception ex) {
                    WriteFile("装柜，并返回装柜状态出错！错误为：" + ex.Message);
                }

            });
            return result;
        }

        private void CreateAlreadyExcel() {
            try {
                var fileName = GetFileName();
                if (!File.Exists(fileName)) {
                    var newFile = new FileInfo(fileName);
                    package = new ExcelPackage(newFile);
                    using (ExcelPackage excelPackage = new ExcelPackage(newFile)) {
                        excelPackage.Workbook.Worksheets[0].Select();
                        var excelWorksheet = excelPackage.Workbook.Worksheets[lblBoxNo.Text];
                        if (excelWorksheet == null) {
                            excelWorksheet = excelPackage.Workbook.Worksheets.Add(lblBoxNo.Text);
                        }
                        excelWorksheet.Cells.Style.ShrinkToFit = true;
                        excelPackage.Save();
                    }
                }
            } catch (Exception ex) {
                WriteFile("创建Excel文件出错！错误为：" + ex.Message);
                MessageBox.Show("请关闭文件后再点击打开！", "错误提示", MessageBoxButtons.OK, MessageBoxIcon.Hand);
            }
        }

        private void SaveAlreadyInfo() {
            var fileName = GetFileName();
            Path.GetFileName(fileName);
            try {
                if (bBusy) {
                    WriteFile("SaveAlreadyInfo() is busy !");
                } else {
                    bBusy = true;
                    var newFile = new FileInfo(fileName);
                    using (ExcelPackage excelPackage = new ExcelPackage(newFile)) {
                        var excelWorksheet = excelPackage.Workbook.Worksheets[lblBoxNo.Text];
                        if (excelWorksheet != null) {
                            excelPackage.Workbook.Worksheets.Delete(lblBoxNo.Text);
                        }
                        excelWorksheet = excelPackage.Workbook.Worksheets.Add(lblBoxNo.Text);
                        excelWorksheet.Cells["A1"].Value = "EV号码";
                        excelWorksheet.Cells["B1"].Value = "箱数";
                        excelWorksheet.Cells["C1"].Value = "已装箱数";
                        excelWorksheet.Column(2).Style.Numberformat.Format = "#,##0";
                        excelWorksheet.Column(3).Style.Numberformat.Format = "#,##0";
                        for (int i = 0; i < gvData.Rows.Count - 1; i++) {
                            excelWorksheet.Cells["A" + (i + 2)].Value = gvData.Rows[i].Cells[0].Value;
                            excelWorksheet.Cells["B" + (i + 2)].Value = gvData.Rows[i].Cells[1].Value;
                            excelWorksheet.Cells["C" + (i + 2)].Value = gvData.Rows[i].Cells[2].Value;
                            excelWorksheet.Column(1).Width = 14.0;
                            excelWorksheet.Column(2).Width = 8.0;
                            excelWorksheet.Column(3).Width = 12.0;
                        }
                        excelPackage.Save();
                    }
                    bBusy = false;
                }
            } catch (Exception ex) {
                WriteFile("保存已装箱商品信息！错误为：" + ex.Message);
            }
        }

        private void SaveAlreadyItemTimeInfo(string sheetName) {
            var fileName = GetFileName();
            Path.GetFileName(fileName);
            try {
                if (bBusy) {
                    WriteFile("SaveAlreadyItemTimeInfo() is busy !");
                } else {
                    bBusy = true;
                    var newFile = new FileInfo(fileName);
                    using (ExcelPackage excelPackage = new ExcelPackage(newFile)) {
                        var excelWorksheet = excelPackage.Workbook.Worksheets[sheetName];
                        if (excelWorksheet == null) {
                            excelWorksheet = excelPackage.Workbook.Worksheets.Add(sheetName);
                        }
                        excelWorksheet.Cells["A1"].Value = "装柜时间";
                        excelWorksheet.Column(1).Width = 22.0;
                        excelWorksheet.Column(1).Style.Numberformat.Format = "yyyy-MM-dd HH:mm:ss fff";
                        excelWorksheet.Column(1).Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        var end = excelWorksheet.Dimension.End;
                        excelWorksheet.Cells["A" + (end.Row + 1)].Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss fff");
                        excelPackage.Save();
                    }
                    bBusy = false;
                }
            } catch (Exception ex) {
                WriteFile("保存已装箱商品时间信息出错！错误为：" + ex.Message);
            }
        }

        private string GetFileName() {
            Path.GetExtension(strFilePath);
            return strFilePath.Substring(0, strFilePath.LastIndexOf("\\")) + "\\" + lblBoxNo.Text + "柜装箱记录表.xlsx";
        }

        private void frmMain_FormClosing(object sender, FormClosingEventArgs e) {
            isCameraReading = false;
            Thread.Sleep(2000);
        }

        public void WriteFile(string sData) {
            try {
                var path = string.Concat(new string[] {
                    Application.StartupPath,
                    lblBoxNo.Text,
                    "_",
                    DateTime.Now.ToString("yyyyMMdd"),
                    ".log"
                });
                if (lblBoxNo.Text == "") {
                    path = "C:\\PLC_" + DateTime.Now.ToString("yyyyMMdd") + ".log";
                }

                var fileStream = new FileStream(path, FileMode.Append);
                var encoding = Encoding.GetEncoding("gb2312");
                var streamWriter = new StreamWriter(fileStream, encoding);
                streamWriter.Write(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss fff") + "\t\t" + sData + "\r\n");
                streamWriter.Flush();
                streamWriter.Close();
                fileStream.Close();
            } catch (Exception ex) {
                MessageBox.Show("写日志文件出现了错：" + ex.Message);
            }
        }

        private void btnOpenExcel_Click(object sender, EventArgs e) {
            openExcel();
        }

        private void writeLog(string msg) {
            Invoke((MethodInvoker)delegate {
                var count = lbxLog.Items.Count;
                if (count > 1000) {
                    lbxLog.Items.RemoveAt(count - 1);
                }


                var s = $"[{DateTime.Now.ToString(DATE_FMT)}] {msg}";
                lbxLog.Items.Insert(0, s);
            });
        }

        private void startReadCamera(Conf conf) {
            isCameraReading = true;
            Task.Run(() => {
                var weiter = new Weighter(conf.weigh.port, conf.weigh.baudrate);
                try {
                    using (codereader = new BarCodeReader(conf.camera.ip, conf.camera.port)) {
                        while (isCameraReading) {
                            var code = codereader.ReadLine();
                            code = stripBarcode(code);

                            if (!string.IsNullOrEmpty(code)) {
                                // 称重
                                var w = weiter.ReadWeight();

                                // handle the code.
                                if (HandleBarCode(code)) {
                                    // 比较
                                    var weightOk = false;
                                    if (w != null) {
                                        weightOk = HandleWeight(code, w.value);
                                    }

                                    // 显示结果
                                    if (!weightOk) {
                                        showLineInfo(3);
                                    }
                                }
                            }
                            Thread.Sleep(200);
                        }
                    }
                } catch (Exception ex) {
                    isCameraReading = false;
                    writeLog("连接相机失败。");
                } finally {
                    weiter.Close();
                }
            });
        }

        private static int parsePlcResult(string data) {
            var pattern = @"\%\d{2}\$RC(?<state>\d)\d*";
            var r = Regex.Match(data, pattern);
            if (r.Success) {
                var b = int.Parse(r.Groups["state"].Value);
                return b;
            } else {
                // throw new Exception("plc返回值有错: " + data);
                return 0;
            }
        }

        private void startPlcInspection() {
            Task.Run(() => {
                while (runInspecting) {
                    PLC.read_RCS(PLC_SLOT_Y0);
                    var value = PLC.GetPLCData();

                    if (string.IsNullOrEmpty(value)) {
                        Thread.Sleep(100);
                        continue;
                    }

                    var state = parsePlcResult(value);
                    if (state == 0 && isCameraReading) {
                        this.Invoke((MethodInvoker)delegate {
                            btnStop.PerformClick();
                        });
                    }

                    if (state == 1 && !isCameraReading) {
                        this.Invoke((MethodInvoker)delegate {
                            btnRun.PerformClick();
                        });
                    }

                    Thread.Sleep(1000);
                }
                writeLog("传送带检测线程停止。");
            });
            writeLog("启动传送带检测线程。");
        }

        private void plcListener(string msg) {
            writeLog(msg);
        }

        private static void PlcStartLine() {
            PLC.setM(PLC_SLOT_Y0, 1);
            PLC.GetPLCData();
        }

        private void btnRun_Click(object sender, EventArgs e) {
            if (!isCameraReading) {
                isCameraReading = true;
                enabelBtns(isCameraReading);
                startReadCamera(conf);
                // 相机连接有可能失败。
                PlcStartLine();
                Thread.Sleep(100);
            }
        }

        private void enabelBtns(bool isrunning) {
            btnRun.Enabled = !isrunning;
            btnStop.Enabled = isrunning;
        }

        private void stopCameraReading() {
            isCameraReading = false;
            Thread.Sleep(2000);
            enabelBtns(isCameraReading);
        }

        private void btnStop_Click(object sender, EventArgs e) {
            stopCameraReading();
            PLC.setM(PLC_SLOT_Y0, 0);
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e) {
            runInspecting = false;
            Thread.Sleep(1000);
            PLC.Close();
        }

        private void btnStopLine_Click(object sender, EventArgs e) {
            PLC.setM(PLC_SLOT_Y3, 0);
            var data = PLC.GetPLCData();
            writeLog("发出生产线停止命令");
        }

        private void btnStartLine_Click(object sender, EventArgs e) {
            PLC.setM(PLC_SLOT_Y3, 1);
            var data = PLC.GetPLCData();
            writeLog("发出生产线启动命令");
        }

        private static string stripBarcode(string s) {
            if (!string.IsNullOrEmpty(s)) {
                var stx = new char[] { (char)02 };
                return s.TrimStart(stx).TrimEnd();
            } else {
                return s;
            }
        }
    }
}
