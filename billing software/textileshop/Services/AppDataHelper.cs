#nullable enable
using System;
using System.IO;

namespace textileshop.Services
{
    /// <summary>
    /// Manages application data paths for installed desktop apps
    /// Ensures data is stored in a writable, user-specific location
    /// </summary>
    public static class AppDataHelper
    {
        private static readonly string _companyName = "BabyTechnologies";
        private static readonly string _appName = "ThugilERP";

        /// <summary>
        /// Gets the proper folder for user-specific app data (writable after install)
        /// </summary>
        public static string GetDataFolderPath()
        {
            string? dataFolder = null;
            Exception? lastError = null;

            // Try 1: AppData\Roaming (preferred for roaming profiles)
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                dataFolder = Path.Combine(appData, _companyName, _appName);
                Directory.CreateDirectory(dataFolder);

                // Verify we can write to it
                string testFile = Path.Combine(dataFolder, ".write_test");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);

                System.Diagnostics.Debug.WriteLine($"[INFO] Data folder verified: {dataFolder}");
                return dataFolder;
            }
            catch (Exception ex)
            {
                lastError = ex;
                System.Diagnostics.Debug.WriteLine($"[WARN] AppData/Roaming failed: {ex.Message}");
            }

            // Try 2: AppData\Local (fallback)
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                dataFolder = Path.Combine(localAppData, _companyName, _appName);
                Directory.CreateDirectory(dataFolder);

                string testFile = Path.Combine(dataFolder, ".write_test");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);

                System.Diagnostics.Debug.WriteLine($"[INFO] Data folder verified (Local): {dataFolder}");
                return dataFolder;
            }
            catch (Exception ex)
            {
                lastError = ex;
                System.Diagnostics.Debug.WriteLine($"[WARN] AppDat/Local failed: {ex.Message}");
            }

            // Try 3: Current directory (last resort - may fail after install)
            try
            {
                dataFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                Directory.CreateDirectory(dataFolder);

                string testFile = Path.Combine(dataFolder, ".write_test");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);

                System.Diagnostics.Debug.WriteLine($"[INFO] Data folder verified (BaseDir): {dataFolder}");
                return dataFolder;
            }
            catch (Exception ex)
            {
                lastError = ex;
                System.Diagnostics.Debug.WriteLine($"[ERROR] All data folder attempts failed: {ex.Message}");
            }

            // If all fail, throw with details
            throw new InvalidOperationException(
                $"Could not create writable data folder. Last error: {lastError?.Message}\n" +
                $"Tried:\n" +
                $"1. %AppData%\\Roaming\\{_companyName}\\{_appName}\n" +
                $"2. %AppData%\\Local\\{_companyName}\\{_appName}\n" +
                $"3. {AppDomain.CurrentDomain.BaseDirectory}\\data",
                lastError);
        }

        /// <summary>
        /// Gets the full path for a specific data file
        /// </summary>
        public static string GetDataFilePath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("File name cannot be empty", nameof(fileName));

            // Remove any path traversal attempts for security
            fileName = Path.GetFileName(fileName);

            return Path.Combine(GetDataFolderPath(), fileName);
        }

        /// <summary>
        /// Gets the bills folder path
        /// </summary>
        public static string GetBillsFolderPath()
        {
            string billsFolder = Path.Combine(GetDataFolderPath(), "bills");
            Directory.CreateDirectory(billsFolder);
            return billsFolder;
        }

        /// <summary>
        /// Test if the data folder is writable (for debugging)
        /// </summary>
        public static bool IsDataFolderWritable(out string? errorMessage)
        {
            try
            {
                string folder = GetDataFolderPath();
                string testFile = Path.Combine(folder, ".writetest_" + Guid.NewGuid().ToString("N"));

                File.WriteAllText(testFile, "test", System.Text.Encoding.UTF8);
                File.Delete(testFile);

                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }
    }
}