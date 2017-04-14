using System;
using System.IO;
using System.IO.Ports;
using System.Text.RegularExpressions;

namespace lgscan {
    public class Weighter {
        private SerialPort comm;

        public Weighter(string port, int baudrate) {
            if (Form1.conf == null) {
                Form1.loadConf();
            }

            comm = new SerialPort {
                PortName = port,
                BaudRate = baudrate,
                Parity = Parity.None,
                DataBits = 8,
                StopBits = StopBits.One,
                NewLine = "\r\n",
                ReadTimeout = 1000,
                WriteTimeout = 1000
            };

            comm.Open();
        }

        public void Close() {
            if (comm.IsOpen) {
                comm.Close();
            }
        }

        /// <summary>
        /// 返回称重值，如果字符串格式不合规格，返回null.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        private static Weight parseValue(string data) {
            const string OK = "OK";

            var pattern = "^(OK|NG):(\\d+\\.\\d+)KG$";

            try {
                var re = new Regex(pattern);
                var m = re.Match(data);
                if (m.Success) {
                    var success = m.Groups[1].Value == OK;
                    var value = double.Parse(m.Groups[2].Value);
                    return new Weight(success, value);
                } else {
                    return null;
                }
            } catch {
                return null;
            }
        }

        /// <summary>
        /// 剥离字符串头部的stx和尾部的etx.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        private static string extractData(string data) {
            const byte stx = 0x02;
            const byte etx = 0x03;

            return data.TrimStart(new char[] { (char)stx })
                .TrimEnd(new char[] { (char)etx });
        }

        /// <summary>
        /// 从称重机读取重量，如果字符串格式不合规格，返回null..
        /// </summary>
        /// <returns></returns>
        public Weight ReadWeight() {
            var raw = comm.ReadExisting();
            var data = extractData(raw);
            return parseValue(data);
        }
    }

    public class Weight {
        public bool ok { get; set; }
        public double value { get; set; }

        public Weight(bool ok, double value) {
            this.ok = ok;
            this.value = value;
        }
    }
}
