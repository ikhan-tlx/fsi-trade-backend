/* ============================================================================
   2026_08_066_DeactivateProductsAndSubProducts.sql
   ----------------------------------------------------------------------------
   Makes the following unavailable for BOTH transaction selection AND field
   configuration, per request:

     PRODUCT
       * HS Code Pricelist  (TmX_Product Product_ID = 4, code HSCODE_PRICELIST)

     EXPORT sub-products (Product 3)
       * Export WEBOC EFE          (selector checkbox EWCheckboxH, no dest tab)
       * Export Drawings           (selector checkbox EDCheckboxB -> Tab 25)

     IMPORT sub-product (Product 2)
       * Import Collection Lodgement (selector checkbox ICLCheckbox -> Tab 15)

   HOW each surface is gated (verified against the code):
     - Product dropdowns / wizard product step -> GET /Product/list + /ActiveLov
       filter TmX_Product.Active_Flag = 1.                -> deactivate product.
     - Field-config product list               -> GET /Product/list (same).
     - Sub-product SELECTION (wizard sub-type step) is built from the active
       `ProductTab` checkbox fields returned by /Product/{id}/Tabs/{sel}/Fields;
       the FE ignores Visibility and keys only on Is_Active. The 3 target
       checkboxes are Is_Active=1 (Visibility=0), so they still show.
                                                          -> deactivate checkbox.
     - Field CONFIG tabs + sub-product tab list -> FormDefinition / Tabs filter
       TmX_Entity_Tab_Product_Mapping.Is_Active = 1.      -> deactivate mapping.

   NOTE on "Export WEBOC EFE": its selector checkbox (EWCheckboxH) has NULL
   Udf_Data — it has no dedicated destination sub-product tab. It lives on the
   Export selector/landing tab (Tab 21 = FCCM Information Export, locale label
   "Export WEBOC EFE"), which ALSO hosts every other Export sub-product
   checkbox. We therefore deactivate only the checkbox (removes it from
   selection) and DELIBERATELY do NOT deactivate Tab 21's mapping, otherwise
   /Product/3/Tabs/21/Fields would 404 and the whole Export sub-product selector
   would break.

   Idempotent + re-runnable (only flips currently-active rows). Rollback at end.
   Apply against ICBC_DEMO (or UAT) with sqlcmd. Restart the API afterwards to
   evict the 15-min in-memory product/tabs/fields caches.
   ============================================================================ */
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @who nvarchar(100) = N'System (2026_08_066 deactivate products/sub-products)';
DECLARE @now datetime      = GETUTCDATE();

/* ---- 1) Deactivate the HS Code Pricelist product -------------------------- */
UPDATE dbo.TmX_Product
SET    Active_Flag = 0, Last_Updated_By = @who, Last_Updated_Date = @now
WHERE  Product_ID = 4            -- HSCODE_PRICELIST
  AND  Active_Flag = 1;
PRINT CONCAT('(1) HS Code Pricelist product deactivated: ', @@ROWCOUNT, ' row(s)');

/* ---- 2) Deactivate the sub-product SELECTOR checkboxes --------------------- */
--     552810 ICLCheckbox  (Import Collection Lodgement)
--     552795 EDCheckboxB  (Export Drawings)
--     552801 EWCheckboxH  (Export WEBOC EFE)
UPDATE dbo.TmX_Tenant_Field_Setup
SET    Is_Active = 0, Last_Updated_By = @who, Last_Updated_Date = @now
WHERE  Tenant_Field_Setup_Id IN (552810, 552795, 552801)
  AND  Field_Lookup_Type = 'ProductTab'
  AND  Is_Active = 1;
PRINT CONCAT('(2) Sub-product selector checkboxes deactivated: ', @@ROWCOUNT, ' row(s)');

/* ---- 3) Deactivate the sub-product TAB MAPPINGS (field config + tab list) -- */
--     (Product 2, Tab 15) Import Collection Lodgment
--     (Product 3, Tab 25) Export Drawings
--     Export WEBOC EFE has no dedicated tab (see header) -> nothing to do.
UPDATE dbo.TmX_Entity_Tab_Product_Mapping
SET    Is_Active = 0, Last_Updated_By = @who, Last_Updated_Date = @now
WHERE  ((Product_Id = 2 AND Tab_Id = 15) OR (Product_Id = 3 AND Tab_Id = 25))
  AND  Is_Active = 1;
PRINT CONCAT('(3) Sub-product tab mappings deactivated: ', @@ROWCOUNT, ' row(s)');

/* ---- 4) Belt-and-braces: deactivate the sub-product TAB rows, but ONLY if no
          other product still maps them active (avoids hiding a shared tab) --- */
UPDATE t
SET    t.Active_Flag = 0, t.Last_Updated_By = @who, t.Last_Updated_Date = @now
FROM   dbo.TmX_Tab t
WHERE  t.Tab_Id IN (15, 25)
  AND  t.Active_Flag = 1
  AND  NOT EXISTS (SELECT 1 FROM dbo.TmX_Entity_Tab_Product_Mapping m
                   WHERE m.Tab_Id = t.Tab_Id AND m.Is_Active = 1);
PRINT CONCAT('(4) Orphaned sub-product tab rows deactivated: ', @@ROWCOUNT, ' row(s)');

/* ---- Confirmation --------------------------------------------------------- */
PRINT '';
PRINT 'Product 4 active flag (expect 0):';
SELECT Product_ID, Product_Name, Active_Flag FROM dbo.TmX_Product WHERE Product_ID = 4;

PRINT 'Target selector checkboxes (expect Is_Active=0):';
SELECT Tenant_Field_Setup_Id, Field_Name, Field_Label, Is_Active
FROM dbo.TmX_Tenant_Field_Setup WHERE Tenant_Field_Setup_Id IN (552810, 552795, 552801);

PRINT 'Target tab mappings (expect Is_Active=0):';
SELECT Product_Id, Tab_Id, Is_Active FROM dbo.TmX_Entity_Tab_Product_Mapping
WHERE (Product_Id=2 AND Tab_Id=15) OR (Product_Id=3 AND Tab_Id=25);

PRINT '';
PRINT '2026_08_066_DeactivateProductsAndSubProducts applied.';

/* ----------------------------------------------------------------------------
   ROLLBACK (manual, dev only):
     UPDATE dbo.TmX_Product SET Active_Flag=1 WHERE Product_ID=4;
     UPDATE dbo.TmX_Tenant_Field_Setup SET Is_Active=1
       WHERE Tenant_Field_Setup_Id IN (552810,552795,552801);
     UPDATE dbo.TmX_Entity_Tab_Product_Mapping SET Is_Active=1
       WHERE (Product_Id=2 AND Tab_Id=15) OR (Product_Id=3 AND Tab_Id=25);
     -- Tab 25 was active before; Tab 15 was already inactive:
     UPDATE dbo.TmX_Tab SET Active_Flag=1 WHERE Tab_Id=25;
   ---------------------------------------------------------------------------- */
