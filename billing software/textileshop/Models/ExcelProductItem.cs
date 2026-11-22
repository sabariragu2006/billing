#nullable enable
using System;
namespace textileshop.Models
{
    public class ExcelProductItem
    {
        public string Id { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public int Qty { get; set; }
        public double Rate { get; set; }   // Sales rate
        public double PurchasePrice { get; set; }   // NEW: cost per unit
        public double ProfitAmount { get; set; }   // NEW: Rate - PurchasePrice
        public double ProfitPct { get; set; }   // NEW: (Profit/Purchase)*100
        public double CostTotal { get; set; }
        public double CGSTPct { get; set; }
        public double CGSTAmt { get; set; }
        public double SGSTPct { get; set; }
        public double SGSTAmt { get; set; }
        public double NetRate { get; set; }
        public string ImportedAt { get; set; } = string.Empty;
        public string? SupplierName { get; set; }
        public string? SupplierContact { get; set; }
        public string? SupplierId { get; set; }

        public static string GenerateNextProductId(string productsJsonPath)
        {
            try
            {
                var existing = new System.Collections.Generic.List<ExcelProductItem>();
                if (System.IO.File.Exists(productsJsonPath))
                {
                    var json = System.IO.File.ReadAllText(productsJsonPath);
                    existing = Newtonsoft.Json.JsonConvert
                        .DeserializeObject<System.Collections.Generic.List<ExcelProductItem>>(json)
                        ?? new System.Collections.Generic.List<ExcelProductItem>();
                }
                int maxNum = 0;
                foreach (var p in existing)
                {
                    if (!string.IsNullOrEmpty(p.Id) && p.Id.StartsWith("PRD-"))
                    {
                        if (int.TryParse(p.Id.Substring(4), out int num) && num > maxNum)
                            maxNum = num;
                    }
                }
                return $"PRD-{(maxNum + 1):D5}";
            }
            catch
            {
                return $"PRD-{DateTime.Now:yyyyMMddHHmmssfff}";
            }
        }
    }
}