using System;
using System.Net.Sockets;
using System.IO;

namespace lgscan {
    class BarCodeReader : IDisposable {
        private TcpClient sock;
        private StreamReader reader;

        public BarCodeReader(string ip, int port) {
            sock = new TcpClient(ip, port) {
                ReceiveTimeout = 1000
            };
            reader = new StreamReader(sock.GetStream());
        }

        public string ReadLine() {
            try {
                return reader.ReadLine();
            } catch {
                return "";
            }
        }

        public void Dispose() {
            if (sock != null && sock.Connected) {
                if (reader != null) {
                    reader.Close();
                }
                sock.Close();
            }
            GC.SuppressFinalize(this);
        }
    }
}
