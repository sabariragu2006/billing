-- ✅ CREATE INVOICES TABLE (if not exists)
IF OBJECT_ID('dbo.Invoices', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Invoices (
        Id INT PRIMARY KEY IDENTITY(1,1),
        InvoiceNumber NVARCHAR(50) NOT NULL UNIQUE,
        InvoiceDate DATE NOT NULL,
        
        -- Buyer Details
        BuyerName NVARCHAR(200) NOT NULL,
        BuyerAddress NVARCHAR(500),
        BuyerMobile NVARCHAR(20),
        BuyerPhone NVARCHAR(20),
        BuyerEmail NVARCHAR(100),
        BuyerGSTIN NVARCHAR(20),
        
        -- Financial Totals
        GrossAmount DECIMAL(18,2) NOT NULL,
        CGSTAmount DECIMAL(18,2) DEFAULT 0,
        SGSTAmount DECIMAL(18,2) DEFAULT 0,
        IGSTAmount DECIMAL(18,2) DEFAULT 0,
        TotalGST DECIMAL(18,2) DEFAULT 0,
        RoundOff DECIMAL(18,2) DEFAULT 0,
        FinalTotal DECIMAL(18,2) NOT NULL,
        
        -- Metadata
        PdfPath NVARCHAR(500),
        PdfUploadDate DATETIME DEFAULT GETDATE(),
        ExtractedFromPdf BIT DEFAULT 1,
        CreatedDate DATETIME DEFAULT GETDATE(),
        UpdatedDate DATETIME
    );
    
    -- Create indexes
    CREATE INDEX IX_Invoices_InvoiceNumber ON dbo.Invoices(InvoiceNumber);
    CREATE INDEX IX_Invoices_BuyerGSTIN ON dbo.Invoices(BuyerGSTIN);
    
    PRINT '✅ Invoices table created';
END
ELSE
BEGIN
    PRINT 'ℹ️ Invoices table already exists';
END
GO

-- ✅ CREATE INVOICE ITEMS TABLE (if not exists)
IF OBJECT_ID('dbo.InvoiceItems', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.InvoiceItems (
        Id INT PRIMARY KEY IDENTITY(1,1),
        InvoiceId INT FOREIGN KEY REFERENCES dbo.Invoices(Id) ON DELETE CASCADE,
        
        -- Product Details
        HSNCode NVARCHAR(20),
        ProductName NVARCHAR(200) NOT NULL,
        Category NVARCHAR(50),
        
        -- Pricing & Quantity
        Quantity INT NOT NULL,
        Rate DECIMAL(18,2) NOT NULL,
        LineTotal DECIMAL(18,2) NOT NULL,
        
        -- Tax Breakdown
        CGSTPercent DECIMAL(5,2) DEFAULT 0,
        CGSTAmount DECIMAL(18,2) DEFAULT 0,
        SGSTPercent DECIMAL(5,2) DEFAULT 0,
        SGSTAmount DECIMAL(18,2) DEFAULT 0,
        IGSTPercent DECIMAL(5,2) DEFAULT 0,
        IGSTAmount DECIMAL(18,2) DEFAULT 0,
        
        -- Link to Products table
        ProductId INT NULL FOREIGN KEY REFERENCES dbo.Products(Id),
        
        CreatedDate DATETIME DEFAULT GETDATE()
    );
    
    -- Create indexes
    CREATE INDEX IX_InvoiceItems_ProductName ON dbo.InvoiceItems(ProductName);
    CREATE INDEX IX_InvoiceItems_InvoiceId ON dbo.InvoiceItems(InvoiceId);
    
    PRINT '✅ InvoiceItems table created';
END
ELSE
BEGIN
    PRINT 'ℹ️ InvoiceItems table already exists';
END
GO