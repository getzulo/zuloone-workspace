SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

UPDATE PaymentTerm
SET Name = CASE Days
        WHEN 7 THEN '7 days'
        WHEN 14 THEN '14 days'
        WHEN 30 THEN '30 days'
        WHEN 45 THEN '45 days'
        WHEN 60 THEN '60 days'
        WHEN 90 THEN '90 days'
        ELSE Name
    END,
    ModifiedDateTime = SYSUTCDATETIME()
WHERE Name IN ('Net 7', 'Net 14', 'Net 30', 'Net 45', 'Net 60', 'Net 90');

UPDATE t
SET Value = CASE p.Days
        WHEN 7 THEN N'7 дней'
        WHEN 14 THEN N'14 дней'
        WHEN 30 THEN N'30 дней'
        WHEN 45 THEN N'45 дней'
        WHEN 60 THEN N'60 дней'
        WHEN 90 THEN N'90 дней'
        ELSE t.Value
    END,
    ModifiedDateTime = SYSUTCDATETIME()
FROM ValueTranslations t
JOIN PaymentTerm p ON p.MetaId = t.RecordMetaId
WHERE t.LanguageCode = 'ru'
  AND t.Value IN (N'Нетто 7', N'Нетто 14', N'Нетто 30', N'Нетто 45', N'Нетто 60', N'Нетто 90');

UPDATE t
SET Value = CASE p.Days
        WHEN 7 THEN N'7 أيام'
        WHEN 14 THEN N'14 يوماً'
        WHEN 30 THEN N'30 يوماً'
        WHEN 45 THEN N'45 يوماً'
        WHEN 60 THEN N'60 يوماً'
        WHEN 90 THEN N'90 يوماً'
        ELSE t.Value
    END,
    ModifiedDateTime = SYSUTCDATETIME()
FROM ValueTranslations t
JOIN PaymentTerm p ON p.MetaId = t.RecordMetaId
WHERE t.LanguageCode = 'ar'
  AND t.Value IN (N'صافي 7', N'صافي 14', N'صافي 30', N'صافي 45', N'صافي 60', N'صافي 90');

INSERT INTO ValueTranslations (MetaId, PropertyMetaId, RecordMetaId, LanguageCode, Value, ModifiedDateTime)
SELECT NEWID(), src.PropertyMetaId, src.RecordMetaId, 'uk',
    CASE p.Days
        WHEN 0 THEN N'Одразу'
        WHEN 7 THEN N'7 днів'
        WHEN 14 THEN N'14 днів'
        WHEN 30 THEN N'30 днів'
        WHEN 45 THEN N'45 днів'
        WHEN 60 THEN N'60 днів'
        WHEN 90 THEN N'90 днів'
    END,
    SYSUTCDATETIME()
FROM ValueTranslations src
JOIN PaymentTerm p ON p.MetaId = src.RecordMetaId
WHERE src.LanguageCode = 'ru'
  AND p.Days IN (0, 7, 14, 30, 45, 60, 90)
  AND NOT EXISTS (
      SELECT 1 FROM ValueTranslations x
      WHERE x.RecordMetaId = src.RecordMetaId
        AND x.PropertyMetaId = src.PropertyMetaId
        AND x.LanguageCode = 'uk'
  );

SELECT Name, Days FROM PaymentTerm ORDER BY Days;
SELECT p.Days, t.LanguageCode, t.Value
FROM ValueTranslations t
JOIN PaymentTerm p ON p.MetaId = t.RecordMetaId
ORDER BY p.Days, t.LanguageCode;
