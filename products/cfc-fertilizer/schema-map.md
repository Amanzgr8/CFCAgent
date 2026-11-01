# CFC Fertilizer Database – Schema Map

Reference for the AI assistant. Read this before writing any SQL against the `fertilizer` database.
Items marked **[TO CONFIRM]** are educated guesses that a product expert still needs to verify.

---

## 1. Ground rules

- **No foreign keys are defined in the database.** Tables are linked only by matching ID column names (for example `product.productid` = `productingredient.productid`). Use the joins listed in section 3.
- **Yes/No flags are `smallint`** (`isactive`, `isinstock`, `isliquid`, `isbindingagent`, ...). Data imported from the old VB6 program may store "yes" as `-1` instead of `1`, so test with `<> 0` for yes and `= 0` for no. Treat NULL as no.
- **Use codes and names when talking to users**, not internal IDs. Users know `ingredientcode`, `productcode` and `customercode`, not `ingredientid`.
- **Identify nutrients by `nutrient.nutrientnumber`, not `nutrientid`.** The numbers match the app: 1 = Total Nitrogen, 6 = Phosphate (P2O5), 7 = Potassium (K2O). These three are required and always active.
- **All nutrient values are percentages.**
- **Stored solutions can be out of date.** A formula's solution amounts and status were saved the last time someone clicked Formulate in the app. If ingredient costs, stock or restrictions changed after `product.updatestamp`, say the solution may be stale and that the formula should be re-formulated in the app.
- **Units depend on system settings** (US, Canadian or metric; see section 2.4). Always state units in answers.
- **Do not query the admin tables** in section 2.6 (users, passwords, license data).
- **Several columns and one whole table are dead VB6 holdovers with no live behavior in the current app** — confirmed by reading the application source code, not guessed. Never use these to answer questions, even though they still hold data: `ingredient.marketcost`, `ingredient.moisture`, `ingredient.round`, `ingredient.mixscale`, `ingredient.alternativecode`, `product.batchsize`, `product.poundpergallon`, `productnutrient.restcost`, the `config` table, the `lpfertilizerconstraint` table, and the pricing tables (`pricecontrol`, `pricelevel`, `productprice`, `priceflag`).

---

## 2. Tables by area

### 2.1 Ingredients and nutrients

**`ingredient`** – raw materials used in formulas (urea, potash, filler, binders, ...)

| Column | Meaning |
|---|---|
| `ingredientid` | Internal ID (primary key) |
| `ingredientcode` | Code shown in the app (up to 15 characters) |
| `ingredientname` | Name |
| `isliquid` | 0 = dry ingredient, non-zero = liquid. Dry ingredients are used only in dry formulas, liquid only in liquid formulas |
| `owncost` | **The cost the optimizer actually uses.** Confirmed from the app's source: the solver reads only `owncost` when formulating (`Product.cs`, `ConstructData`/`DoLP`). Always stored normalized to cost-per-pound (cost-per-kg under Canadian units), regardless of what cost unit is displayed on screen ($/Ton, $/100Lb, etc. — those are purely for data entry/display, converted to/from this base unit on save) |
| `marketcost` | **Dead/legacy column — do not use for cost questions.** It's saved and migrated from the old VB6 data but never read by the solver or any other calculation in the current app. There is no "own vs. market" toggle in the running code (`config.isowning` and the lookalike `FlagInfo.IsLeastCostOwning` both exist as fields but are never used anywhere) |
| `bulkdensity` | Dry ingredients: lbs per cubic foot. Needed for the blend's approximate bulk density |
| `poundpergallon` | Liquid ingredients: density in lbs/gallon (kg/litre in metric). **Required** for every liquid ingredient, or liquid formulas are rejected |
| `isinstock` | 0 = out of stock (never used in a solution) |
| `isactive` | 0 = inactive (cannot be added to formulas). Ingredients are never deleted, only deactivated |
| `isbindingagent` | Non-zero = this ingredient is a binder. Binders are not offered in a formula's ingredient list; the app adds them automatically |
| `bindingagentid` | The binder this ingredient needs → `ingredient.ingredientid` of the binder |
| `ingredientbaseamount` | Base amount (lbs) of this ingredient that the binder ratio applies to |
| `ingredientrequiredamount` | Amount of binder required per base amount (lbs, or gallons for a liquid binder) |
| `moisture`, `round`, `mixscale`, `alternativecode` | **Dead/legacy columns — do not use.** Mapped in the app's code but never read by any screen, report or calculation; they're carried over from the old VB6 data format only |
| `updatestamp`, `update_cfcuserid` | When and by whom the row was last saved |

**`nutrient`** – nutrient definitions

| Column | Meaning |
|---|---|
| `nutrientid` | Internal ID |
| `nutrientnumber` | Number shown in the app (1 = N, 6 = P2O5, 7 = K2O) |
| `nutrientname` | Name |
| `unit` | Unit (percent) |
| `isactive` | Only active nutrients are offered on formulation screens |

**`ingredientnutrientvalue`** – nutrient profile of each ingredient (one row per ingredient per nutrient)

| Column | Meaning |
|---|---|
| `ingredientid` | → `ingredient.ingredientid` |
| `nutrientid` | → `nutrient.nutrientid` |
| `value` | Percent of that nutrient in the ingredient |

### 2.2 Formulas (called "products" in the database)

**`product`** – one row per formula

| Column | Meaning |
|---|---|
| `productid` | Internal ID |
| `productcode`, `productname` | Code (up to 15 characters) and name shown in the app |
| `customerid` | Customer the formula is prepared for → `customer.customerid` |
| `producttype` | Formula method and form: 1 = dry soil test, 2 = dry to grade, 3 = liquid soil test, 4 = liquid to grade |
| `formulationstatus` | Result of the last Formulate: 0 or 1 = not formulated, 2 = feasible, 3 = infeasible |
| `fielddescription` | Field ID/description |
| `numberacre` | Number of acres (hectares) to treat |
| `appraterequired` | Application rate entered by the user (lbs/acre). 0 or NULL = let the optimizer choose the minimum |
| `labrefrencenumber` | Lab reference number (note the spelling in the column name) |
| `batchsize` | **Dead/legacy column — do not use.** Confirmed from source: the current app never reads or writes this column outside the one-time VB6 import. The report batch size always comes from `systemoption.batchsize`/`liquidbatchsize` (with an optional one-off, never-saved override via the "Change Batch Size" dialog when printing) — it does not come from, or get saved back to, this column |
| `poundpergallon` | **Dead/legacy column — do not use.** Confirmed from source: only ever set by the VB6 migrator. The app instead computes the blend's density live on every load/report, as a weighted average of the ingredients' own `bulkdensity`/`poundpergallon` by their `solution` share — this column is not that live value and isn't kept in sync with it |
| `reportcomments` | Description/comments printed on the report |
| `isactive` | 0 = inactive. Formulas are never deleted |
| `updatestamp`, `update_cfcuserid` | Last saved (the app saves automatically on Formulate) |

**`productnutrient`** – nutrient requirements of a formula

| Column | Meaning |
|---|---|
| `productid` | → `product.productid` |
| `nutrientid` | → `nutrient.nutrientid` |
| `minimum`, `maximum` | Soil test formulas (`producttype` 1, 3): lbs per acre. Grade formulas (`producttype` 2, 4): percent of the mix |
| `restcost` | **Dead/legacy column — do not use.** A shadow-price field carried over from the old VB6 program; the current solver never computes or updates it (a bulk query even hardcodes it to 0) |

**`productingredient`** – ingredients offered to a formula and the solution amounts

| Column | Meaning |
|---|---|
| `productid` | → `product.productid` |
| `ingredientid` | → `ingredient.ingredientid` |
| `minimum`, `maximum` | Optional limits, in the unit given by `productingredientunitid` |
| `productingredientunitid` | → `productingredientunit` (Lbs/Acre, Lbs/Ton or % of Total) |
| `isinstock` | The "In Stock Can Use" checkbox for this formula. 0 = left out of this solution |
| `solution` | Amount of the ingredient in the last solution, in **lbs/acre (US/Canadian) or kg/hectare (metric)** — the ingredient's application rate. Confirmed from source: reports label the formula's total of this column "Application Rate", and the app derives a per-ton figure *from* this value (`Solution / TotalSolution * 2000`), so `solution` itself is not per-ton |

**`productingredientunit`** – lookup of min/max units (`productingredientunitname`: Lbs/Acre, Lbs/Ton, % of Total)

**`productbindingagent`** – binders added to a formula's solution

| Column | Meaning |
|---|---|
| `productid` | → `product.productid` |
| `ingredientid` | The binder → `ingredient.ingredientid` |
| `isinstock` | Binder stock flag |
| `solution` | Amount of binder required, in the **same unit as `productingredient.solution`** (lbs/acre or kg/hectare) — it's computed as a scaled sum of the carrier ingredient's `solution` values |

**`lpfertilizerconstraint`** – constraints built for the optimizer (linear program)

| Column | Meaning |
|---|---|
| `productid`, `nutrientid`, `ingredientid` | What the constraint applies to |
| `colids`, `restrictionlabel`, `minimum`, `maximum` | **This whole table is dead/legacy — do not query it.** Confirmed from source: the solver builds its constraints freshly in memory on every Formulate run (from `productnutrient` and `productingredient`) and never saves them here; the only code that touches this table at all is a bulk-delete during data cleanup. Always use `productnutrient` and `productingredient` for constraint questions |

### 2.3 Customers

**`customer`** – growers or companies formulas are prepared for

| Column | Meaning |
|---|---|
| `customerid` | Internal ID |
| `customercode`, `customername` | Code (up to 15 characters) and name |
| `address1`, `address2`, `city`, `zipcode`, `customerphone` | Contact details |
| `stateid` | → `state.stateid` |
| `isactive` | 0 = inactive. Customers are never deleted |

**`state`** – states/provinces used in addresses (`statename`, `statecode`, `isactive`)

### 2.4 System settings

**`systemoption`** – settings from the System Settings screen (normally one row). **Confirmed the live settings table** — loaded at app startup and read/written throughout the app

| Column | Meaning |
|---|---|
| `ismetric`, `iscanadian` | Measurement units: both 0 = US (lbs, tons, acres); `iscanadian` = Canadian (lbs, metric tons, acres); `ismetric` = metric (kg, metric tons, hectares) |
| `ingredientcostunit` | Dry cost unit: $/Ton, $/100 Lb or $/Pound (metric: $/Tonne, $/100 Kg, $/Kilogram). Liquid costs are per gallon / Imp gallon / litre |
| `isbulkdensityactive` | Compute and report approximate bulk density for dry mixes |
| `batchsize`, `batchnumberdecimal`, `batchtypeorder`, `isbatchround`, `batchroundto`, `numberbatchesinteger` | Dry batch report options |
| `liquidbatchsize`, `liquidbatchnumberdecimal`, `liquidbatchiorder`, `isliquidbatchround`, `liquidbatchroundto`, `isliquidbatchisgallon` | Liquid batch report options (batch size in pounds or gallons) |
| `nutrientdecimaldry`, `nutrientdecimalliquid` | Decimals for grade nutrient requirements (0–6) |
| `isprintformulaandbatch`, `numberofreportprintcopies` | Print options |
| `issingleuser` | Single-user vs multi-user mode |
| `companyname`, `companyaddress1`, `companycity`, `companystate`, `companyzipcode`, `companyphonenumber` | Manufacturer shown on reports |
| `programversion` | App version |

**`config`** – older settings table from the VB6 program. **Dead/legacy — do not use.** Confirmed there's no code in the current app that reads or writes it at all (no corresponding class exists); `systemoption` is what's actually live.

### 2.5 Pricing (not covered in the user guide)

`pricecontrol`, `pricelevel`, `productprice`, `priceflag` – price levels, rounding of bulk/package prices and a pricing-module trial flag. **Dead/legacy module — do not use.** Confirmed the entity classes for these tables exist in the code but are never instantiated or wired into any screen or menu; they're untouched except during VB6-import cleanup.

### 2.6 Admin tables – do not query

| Table | Why |
|---|---|
| `cfcuser` | App users and **passwords** |
| `license` | License keys and signatures |
| `version`, `updatecheck` | Internal app housekeeping |

---

## 3. How tables join

```
customer ──< product ──< productnutrient >── nutrient
   │            │
 state          ├──< productingredient >── ingredient ──< ingredientnutrientvalue >── nutrient
                │          │                   │
                │   productingredientunit      └── bindingagentid → ingredient (the binder)
                │
                └──< productbindingagent >── ingredient (the binder)
```

| From | To | Join |
|---|---|---|
| product | customer | `product.customerid = customer.customerid` |
| customer | state | `customer.stateid = state.stateid` |
| product | productnutrient | `productnutrient.productid = product.productid` |
| productnutrient | nutrient | `productnutrient.nutrientid = nutrient.nutrientid` |
| product | productingredient | `productingredient.productid = product.productid` |
| productingredient | ingredient | `productingredient.ingredientid = ingredient.ingredientid` |
| productingredient | productingredientunit | `productingredient.productingredientunitid = productingredientunit.productingredientunitid` |
| ingredient | ingredientnutrientvalue | `ingredientnutrientvalue.ingredientid = ingredient.ingredientid` |
| ingredientnutrientvalue | nutrient | `ingredientnutrientvalue.nutrientid = nutrient.nutrientid` |
| ingredient | its binder | `ingredient.bindingagentid = binder.ingredientid` (self-join) |
| product | productbindingagent | `productbindingagent.productid = product.productid` |
| any table | user who last saved it | `x.update_cfcuserid = cfcuser.cfcuserid` (select `username` only, never `userpassword`) |

---

## 4. Example queries

**Active, in-stock dry ingredients with costs**
```sql
-- owncost is the real cost; marketcost is a dead legacy column, included
-- here only for completeness and shouldn't be relied on
SELECT ingredientcode, ingredientname, owncost, marketcost, bulkdensity
FROM ingredient
WHERE COALESCE(isactive, 0) <> 0
  AND COALESCE(isinstock, 0) <> 0
  AND COALESCE(isliquid, 0) = 0
ORDER BY ingredientcode;
```

**Nutrient profile of one ingredient**
```sql
SELECT n.nutrientnumber, n.nutrientname, v.value AS percent
FROM ingredient i
JOIN ingredientnutrientvalue v ON v.ingredientid = i.ingredientid
JOIN nutrient n ON n.nutrientid = v.nutrientid
WHERE i.ingredientcode = 'UREA'
  AND COALESCE(v.value, 0) <> 0
ORDER BY n.nutrientnumber;
```

**Active formulas that use an ingredient in their solution**
```sql
SELECT p.productcode, p.productname, c.customername, pi.solution
FROM productingredient pi
JOIN product p    ON p.productid = pi.productid
JOIN ingredient i ON i.ingredientid = pi.ingredientid
LEFT JOIN customer c ON c.customerid = p.customerid
WHERE i.ingredientcode = 'POTASH'
  AND COALESCE(pi.solution, 0) > 0
  AND COALESCE(p.isactive, 0) <> 0
ORDER BY p.productcode;
```

**All formulas for a customer**
```sql
SELECT p.productcode, p.productname,
       CASE p.producttype WHEN 1 THEN 'Dry soil test' WHEN 2 THEN 'Dry to grade'
                          WHEN 3 THEN 'Liquid soil test' WHEN 4 THEN 'Liquid to grade' END AS formula_type,
       CASE p.formulationstatus WHEN 2 THEN 'Feasible' WHEN 3 THEN 'Infeasible'
                                ELSE 'Not formulated' END AS status,
       p.fielddescription, p.numberacre, p.updatestamp
FROM product p
JOIN customer c ON c.customerid = p.customerid
WHERE c.customercode = 'SMITH01'
ORDER BY p.updatestamp DESC;
```

**Nutrient requirements of a formula**
```sql
SELECT n.nutrientnumber, n.nutrientname, pn.minimum, pn.maximum
FROM product p
JOIN productnutrient pn ON pn.productid = p.productid
JOIN nutrient n ON n.nutrientid = pn.nutrientid
WHERE p.productcode = 'ABC123'
ORDER BY n.nutrientnumber;
```

**Ingredients of a formula, with limits and solution**
```sql
-- pi.solution is lbs/acre (US/Canadian) or kg/hectare (metric), not lbs/ton of mix
SELECT i.ingredientcode, i.ingredientname,
       pi.minimum, pi.maximum, u.productingredientunitname AS limit_unit,
       pi.isinstock AS can_use, i.isinstock AS ingredient_in_stock,
       pi.solution, i.owncost
FROM product p
JOIN productingredient pi ON pi.productid = p.productid
JOIN ingredient i ON i.ingredientid = pi.ingredientid
LEFT JOIN productingredientunit u ON u.productingredientunitid = pi.productingredientunitid
WHERE p.productcode = 'ABC123'
ORDER BY pi.solution DESC NULLS LAST;
```

**Binders in a formula's solution**
```sql
SELECT i.ingredientcode AS binder, i.ingredientname, b.solution
FROM product p
JOIN productbindingagent b ON b.productid = p.productid
JOIN ingredient i ON i.ingredientid = b.ingredientid
WHERE p.productcode = 'ABC123';
```

**Liquid ingredients missing a density (these block liquid formulation)**
```sql
SELECT ingredientcode, ingredientname, poundpergallon
FROM ingredient
WHERE COALESCE(isliquid, 0) <> 0
  AND COALESCE(isactive, 0) <> 0
  AND COALESCE(poundpergallon, 0) = 0;
```

**Formulas whose solution may be stale (an ingredient changed after the formula was last saved)**
```sql
SELECT DISTINCT p.productcode, p.productname, p.updatestamp AS formula_saved,
       i.ingredientcode, i.updatestamp AS ingredient_changed
FROM product p
JOIN productingredient pi ON pi.productid = p.productid
JOIN ingredient i ON i.ingredientid = pi.ingredientid
WHERE COALESCE(p.isactive, 0) <> 0
  AND i.updatestamp > p.updatestamp
ORDER BY p.productcode;
```

**Count formulas by status (use to confirm the status codes)**
```sql
SELECT formulationstatus, producttype, COUNT(*)
FROM product
GROUP BY formulationstatus, producttype
ORDER BY 1, 2;
```

---

## 5. Open questions for the product expert

All 7 questions from the previous draft were resolved by reading the application's
source code (`Fertilizer SourceCode/`), not by guessing — see the notes inline in
sections 1, 2.1, 2.2 and 2.4-2.5 above. Summary:

1. ~~`marketcost` vs `owncost`~~ → the solver uses `owncost` only; `marketcost` is dead.
2. ~~`productingredient.solution` / `productbindingagent.solution` unit~~ → lbs/acre (US/Canadian) or kg/hectare (metric).
3. ~~`productnutrient.restcost`~~ → dead VB6 shadow-price column.
4. ~~`systemoption` vs `config`~~ → `systemoption` is live; `config` is dead.
5. ~~`ingredient.moisture`, `round`, `mixscale`, `alternativecode`~~ → all dead/unused.
6. ~~Pricing tables~~ → confirmed unused legacy module.
7. ~~Stored cost unit~~ → `owncost` is always normalized to cost-per-pound/kg regardless of the displayed cost-unit setting.

No open questions remain from source-code review. Two caveats worth a product expert's
sanity check, since they're inferred from code behavior rather than stated outright:

- The exact liquid-vs-dry and Canadian-kg cost conversion math (`Global.OwnCostPerUnit`)
  wasn't independently verified against a real formulation in the app — worth a spot
  check with a known ingredient cost once there's a live connection.
- Whether any customer data actually has non-zero `marketcost`/`restcost`/pricing-table
  rows left over from a VB6 import (harmless either way, since the assistant is told to
  ignore them, but useful to know if those tables turn out to be entirely empty).
