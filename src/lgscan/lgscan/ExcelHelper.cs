using OfficeOpenXml;
using OfficeOpenXml.Table;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
namespace lgscan {
    public class ExcelHelper {
        public static void SaveToExcel<T>(IEnumerable<T> data, string FileName, string OpenPassword = "") {
            var newFile = new FileInfo(FileName);
            try {
                using (ExcelPackage excelPackage = new ExcelPackage(newFile, OpenPassword)) {
                    var excelWorksheet = excelPackage.Workbook.Worksheets.Add(typeof(T).Name);
                    excelWorksheet.Cells["A1"].LoadFromCollection<T>(data, true, TableStyles.Medium10);
                    excelPackage.Save(OpenPassword);
                }
            } catch (InvalidOperationException ex) {
                Console.WriteLine(ex.Message);
            }
        }

        private static IEnumerable<T> LoadFromExcel<T>(string FileName) where T : new() {
            var newFile = new FileInfo(FileName);
            var list = new List<T>();
            var dictionary = new Dictionary<string, int>();
            using (ExcelPackage excelPackage = new ExcelPackage(newFile)) {
                var excelWorksheet = excelPackage.Workbook.Worksheets[1];
                var column = excelWorksheet.Dimension.Start.Column;
                var column2 = excelWorksheet.Dimension.End.Column;
                var row = excelWorksheet.Dimension.Start.Row;
                var row2 = excelWorksheet.Dimension.End.Row;
                for (int i = column; i <= column2; i++) {
                    dictionary[excelWorksheet.Cells[row, i].Value.ToString()] = i;
                }
                var list2 = new List<PropertyInfo>(typeof(T).GetProperties());
                for (int j = row + 1; j < row2; j++) {
                    T item = (default(T) == null) ? Activator.CreateInstance<T>() : default(T);
                    foreach (PropertyInfo current in list2) {
                        try {
                            var excelRange = excelWorksheet.Cells[j, dictionary[current.Name]];
                            if (excelRange.Value != null) {
                                current.PropertyType.Name.ToLower();
                            }
                        } catch { }
                    }
                    list.Add(item);
                }
            }
            return list;
        }
    }
}