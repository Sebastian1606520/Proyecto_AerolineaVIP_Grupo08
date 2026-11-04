USE AerolineaVIP_Identity;
GO
 
IF COL_LENGTH('dbo.AspNetUsers', 'Nombres') IS NULL
    ALTER TABLE dbo.AspNetUsers ADD Nombres NVARCHAR(100) NULL;
GO
 
IF COL_LENGTH('dbo.AspNetUsers', 'Apellidos') IS NULL
    ALTER TABLE dbo.AspNetUsers ADD Apellidos NVARCHAR(100) NULL;
GO
