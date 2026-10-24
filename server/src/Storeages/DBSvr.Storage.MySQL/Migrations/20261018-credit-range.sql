-- Run during maintenance after backing up the characters table.
-- Preserve legacy signed-byte credit values as their logical 0..255 values.
ALTER TABLE characters MODIFY CreditPoint SMALLINT NULL DEFAULT 0;
UPDATE characters SET CreditPoint = CreditPoint + 256 WHERE CreditPoint BETWEEN -128 AND -1;
ALTER TABLE characters MODIFY CreditPoint TINYINT UNSIGNED NULL DEFAULT 0;
