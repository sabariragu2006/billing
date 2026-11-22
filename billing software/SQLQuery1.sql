-- ✅ Products Table
CREATE TABLE Products (
    Id INT PRIMARY KEY IDENTITY(1,1),
    Name NVARCHAR(200) NOT NULL,
    Category NVARCHAR(100) NOT NULL,
    Price DECIMAL(18,2) NOT NULL,
    Quantity INT NOT NULL DEFAULT 0,
    Description NVARCHAR(MAX),
    PdfPath NVARCHAR(500),  -- Store file path, NOT file content
    AddedDate DATETIME DEFAULT GETDATE(),
    UpdatedDate DATETIME
);

-- ✅ Index for faster searches
CREATE INDEX IX_Products_Name ON Products(Name);
CREATE INDEX IX_Products_Category ON Products(Category);

-- ✅ Optional: Sales table for future Page3
CREATE TABLE Sales (
    Id INT PRIMARY KEY IDENTITY(1,1),
    ProductId INT FOREIGN KEY REFERENCES Products(Id),
    QuantitySold INT NOT NULL,
    SalePrice DECIMAL(18,2) NOT NULL,
    SaleDate DATETIME DEFAULT GETDATE(),
    CustomerName NVARCHAR(100)
);