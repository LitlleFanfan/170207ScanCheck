using System;
using System.Collections;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace lgscan {
    public class PLC {
        private delegate void SetTextCallback(string text);

        public static TextBox txtCommLog = new TextBox();

        public static SerialPort COMM = new SerialPort();

        public static double[] DTValue = null;

        public static BitArray[] arrXYMValue = null;

        public static int iDelay = 60;

        public static bool Open(string strPort) {
            bool result;
            try {
                if (PLC.COMM.IsOpen) {
                    PLC.COMM.Close();
                }
                PLC.COMM.PortName = strPort;
                PLC.COMM.BaudRate = 9600;
                PLC.COMM.Parity = Parity.Odd;
                PLC.COMM.DataBits = 8;
                PLC.COMM.StopBits = StopBits.One;
                PLC.COMM.NewLine = "\r\n";
                PLC.COMM.DataReceived += new SerialDataReceivedEventHandler(PLC.OnDataReceived);
                PLC.COMM.Open();
                result = true;
            } catch (Exception ex) {
                MessageBox.Show(ex.Message);
                result = false;
            }
            return result;
        }

        public static void Close() {
            if (PLC.COMM.IsOpen) {
                PLC.COMM.Close();
            }
        }

        private static void SetText(string text) {
            if (PLC.txtCommLog.InvokeRequired) {
                var method = new PLC.SetTextCallback(PLC.SetText);
                PLC.txtCommLog.Invoke(method, new object[]
                {
                    text
                });
                return;
            }
            PLC.txtCommLog.Text = text + "\r\n" + PLC.txtCommLog.Text;
        }

        private static void OnDataReceived(object sender, SerialDataReceivedEventArgs e) {
            var text = "";
            while (PLC.COMM.BytesToRead > 0) {
                var array = new byte[PLC.COMM.BytesToRead];
                PLC.COMM.Read(array, 0, array.Length);
                text += Encoding.ASCII.GetString(array);
            }
            Thread.Sleep(1);
            if (text.Length < 6) {
                return;
            }
            var text2 = text.Substring(0, 4);
            var text3 = text.Substring(4, 2);
            PLC.SetText("[PLC->PC]:" + text);
            string a;
            if ((a = text2) != null) {
                string a2;
                if (!(a == "%01$")) {
                    if (!(a == "%01!")) {
                        return;
                    }
                } else if ((a2 = text3) != null) {
                    if (a2 == "RD") {
                        var text4 = text.Substring(6, text.Length - 8);
                        PLC.DTValue = new double[text4.Length / 4];
                        for (int i = 0; i < text4.Length / 4; i++) {
                            var text5 = text4.Substring(i * 4, 4);
                            PLC.DTValue[i] = (double)Convert.ToInt32(text5.Substring(2, 2) + text5.Substring(0, 2), 16) / 100.0;
                            Console.WriteLine("DT" + i.ToString() + ":" + PLC.DTValue[i].ToString());
                        }
                        return;
                    }
                    if (!(a2 == "RC")) {
                        if (!(a2 == "WD") && !(a2 == "WC")) {
                            return;
                        }
                    } else {
                        var text4 = text.Substring(6, text.Length - 8);
                        PLC.arrXYMValue = new BitArray[text4.Length / 4];
                        for (int i = 0; i < text4.Length / 4; i++) {
                            var text5 = text4.Substring(i * 4, 4);
                            var value = Convert.ToInt32(text5.Substring(2, 2) + text5.Substring(0, 2), 16);
                            var bytes = BitConverter.GetBytes(value);
                            PLC.arrXYMValue[i] = new BitArray(bytes);
                            Console.WriteLine("XYM" + i.ToString() + ":" + PLC.arrXYMValue[i][i].ToString());
                        }
                    }
                }
            }
        }

        public static void setM(string XYMaddr, int value) {
            var str = XYMaddr.Substring(0, 1);
            var str2 = XYMaddr.Substring(1, XYMaddr.Length - 1).PadLeft(4).Replace(" ", "0");
            var text = "%01#WCS" + str + str2 + value.ToString();
            text = text + PLC.bcc(text) + "\r";
            PLC.COMM.Write(text);
            PLC.SetText("[PC->PLC]:" + text);
        }

        public static void read_RCS(string XYMaddr) {
            var str = XYMaddr.Substring(0, 1);
            var str2 = XYMaddr.Substring(1, XYMaddr.Length - 1).PadLeft(4).Replace(" ", "0");
            var text = "%01#RCS" + str + str2;
            text = text + PLC.bcc(text) + "\r";
            PLC.COMM.Write(text);
            PLC.SetText("[PC->PLC]:" + text);
        }

        public static void readMS(string DTaddr1, string DTaddr2) {
            var str = DTaddr1.Substring(0, 1);
            var text = DTaddr1.Substring(1, DTaddr1.Length - 1);
            var text2 = DTaddr2.Substring(1, DTaddr2.Length - 1);
            Convert.ToInt16(text2);
            Convert.ToInt16(text);
            text = text.PadLeft(4).Replace(" ", "0");
            text2 = text2.PadLeft(4).Replace(" ", "0");
            var str2 = str + text + text2;
            var text3 = "%01#RCC" + str2;
            text3 = text3 + PLC.bcc(text3) + "\r";
            PLC.COMM.Write(text3);
            PLC.SetText("[PC->PLC]:" + text3);
        }

        public static void writeDT(string DTaddr1, string DTaddr2, double[] value) {
            var str = DTaddr1.Substring(0, 1);
            var text = DTaddr1.Substring(1, DTaddr1.Length - 1);
            var text2 = DTaddr2.Substring(1, DTaddr2.Length - 1);
            var num = (int)(Convert.ToInt16(text2) - Convert.ToInt16(text) + 1);
            text = text.PadLeft(5).Replace(" ", "0");
            text2 = text2.PadLeft(5).Replace(" ", "0");
            var str2 = str + text + text2;
            var text3 = "";
            for (int i = 0; i < num; i++) {
                var text4 = ((int)(value[i] * 1000.0)).ToString();
                text4 = Convert.ToInt32(text4).ToString("X4");
                text4 = text4.Substring(2, 2) + text4.Substring(0, 2);
                text3 += text4;
            }
            var text5 = "%01#WD" + str2 + text3;
            text5 = text5 + PLC.bcc(text5) + "\r";
            PLC.COMM.Write(text5);
            PLC.SetText("[PC->PLC]:" + text5);
        }

        public static void readDT(string DTaddr1, string DTaddr2) {
            var str = DTaddr1.Substring(0, 1);
            var text = DTaddr1.Substring(1, DTaddr1.Length - 1);
            var text2 = DTaddr2.Substring(1, DTaddr2.Length - 1);
            Convert.ToInt16(text2);
            Convert.ToInt16(text);
            text = text.PadLeft(5).Replace(" ", "0");
            text2 = text2.PadLeft(5).Replace(" ", "0");
            var str2 = str + text + text2;
            var text3 = "%01#RD" + str2;
            text3 = text3 + PLC.bcc(text3) + "\r";
            PLC.COMM.Write(text3);
            PLC.SetText("[PC->PLC]:" + text3);
        }

        private static string bcc(string chkString) {
            var num = 0;
            for (int i = 0; i < chkString.Length; i++) {
                num ^= PLC.Asc(chkString.Substring(i, 1));
            }
            var text = System.Convert.ToString(num, 16);
            return text.Substring(text.Length - 2, 2).ToUpper();
        }

        private static int Asc(string character) {
            if (character.Length == 1) {
                var aSCIIEncoding = new ASCIIEncoding();
                return (int)aSCIIEncoding.GetBytes(character)[0];
            }
            throw new Exception("Character is not valid.");
        }
    }
}
