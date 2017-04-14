using System;
using System.IO;
using System.IO.Ports;

namespace lgscan {
    public class Weighter {
        private static SerialPort comm = new SerialPort();

        public Weighter(string port, int baudrate) {
            try {
                if (Form1.conf == null) {
                    Form1.loadConf();
                }
                if (comm.IsOpen) {
                    comm.Close();
                }
                comm.PortName = port;
                comm.BaudRate = baudrate;
                comm.Parity = Parity.None;
                comm.DataBits = 8;
                comm.StopBits = StopBits.One;
                comm.NewLine = "\r\n";

                comm.ReadTimeout = 1000;
                comm.WriteTimeout = 1000;

                comm.Open();
            } catch (Exception ex) {
            }
        }

        public void Close() {
            if (comm.IsOpen) {
                comm.Close();
            }
        }

        public decimal GetWeight() {
            string w = comm.ReadExisting();

            return 0;
        }
    }
}
