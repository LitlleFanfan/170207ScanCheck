﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hjson;

namespace lgscan {
    public class CameraConf {
        public string ip { get; set; }
        public int port { get; set; }
    }

    public class PlcConf {
        public string port { get; set; }
        public int baudrate { get; set; }
    }

    public class Conf {
        public CameraConf camera { get; set; }
        public PlcConf plc { get; set; }

        public Conf() {
            camera = new CameraConf();
            plc = new PlcConf();
        }

        public override string ToString() {
            var s = $"camera ip: {camera.ip}, camera port: {camera.port}";
            s += $"plc port: {plc.port}, plc baudrate: {plc.baudrate}";
            return s;
        }

        public static Conf loadFile(string path) {
            var conf = new Conf();
            var obj = HjsonValue.Load(path).Qo();
            if (obj != null) {
                conf.camera.ip = obj.Qo("camera").Qs("ip");
                conf.camera.port = obj.Qo("camera").Qi("port");
                conf.plc.port = obj.Qo("plc").Qs("port");
                conf.plc.baudrate = obj.Qo("plc").Qi("baudrate");                
            }
            return conf;
        }
    }
}
