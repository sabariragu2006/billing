#nullable enable
using System;
using System.IO;
using Newtonsoft.Json;
using textileshop.Services;

namespace textileshop.Models
{
    /// <summary>
    /// Application settings stored in JSON file
    /// Supports shop branding: name, subtitle, address, phone, and logo
    /// </summary>
    public class AppSettings
    {
        // ✅ Core branding properties
        public string ShopName { get; set; } = "Tharun Textiles";
        public string? Subtitle { get; set; } = "Inventory Manager";
        public string? ShopAddress { get; set; } // ✅ ADDED
        public string? ShopPhone { get; set; }   // ✅ ADDED
        public string? LogoPath { get; set; }

        public static AppSettings Load()
        {
            try
            {
                return DatabaseHelper.LoadSettings();
            }
            catch
            {
                return new AppSettings(); // Return defaults on failure
            }
        }
        public bool Save()
        {
            try
            {
                return DatabaseHelper.SaveSettings(this);
            }
            catch
            {
                return false;
            }
        }

        // Get settings file path using AppDataHelper for consistent storage
        public static string GetSettingsFilePath()
        {
            return AppDataHelper.GetDataFilePath("app_settings.json");
        }
    }
} 