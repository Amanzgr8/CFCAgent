# Formulas affected by the D066 cost change

Raising the cost of **D066** from `0.41` to `0.43` per pound touches **50 formulas**. The optimizer uses `owncost` only, so every one of them would need to be *re-formulated* before the new price shows up in a cost per ton.

## Affected formulas

| Formula | Grade | Customer | Cost per ton (current) |
|---|---|---|---:|
| F1001 | 10-10-10 | Riverbend Ag | 549.42 |
| F1002 | 28-0-0 | Lakeshore Farms | 478.53 |
| F1003 | 8-32-16 | Summit Fertilizer | 328.15 |
| F1004 | 6-24-24 | Riverbend Ag | 495.32 |
| F1005 | 0-0-60 | Delta Growers | 284.16 |
| F1006 | 6-24-24 | Delta Growers | 434.02 |
| F1007 | 8-32-16 | Lakeshore Farms | 595.55 |
| F1008 | 15-15-15 | Hilltop Farms | 280.78 |
| F1009 | 19-19-19 | Delta Growers | 440.16 |
| F1010 | 19-19-19 | Lakeshore Farms | 439.26 |
| F1011 | 6-24-24 | Greenfield Supply | 456.92 |
| F1012 | 28-0-0 | Oakridge Co-op | 473.89 |
| F1013 | 15-15-15 | Prairie Co-op | 495.70 |
| F1014 | 15-15-15 | Prairie Co-op | 556.99 |
| F1015 | 0-0-60 | Lakeshore Farms | 370.73 |
| F1016 | 28-0-0 | Riverbend Ag | 382.20 |
| F1017 | 28-0-0 | Riverbend Ag | 335.15 |
| F1018 | 10-10-10 | Oakridge Co-op | 499.89 |
| F1019 | 28-0-0 | Lakeshore Farms | 345.53 |
| F1020 | 19-19-19 | Prairie Co-op | 561.46 |
| F1021 | 12-12-12 | Hilltop Farms | 308.56 |
| F1022 | 6-24-24 | Greenfield Supply | 412.49 |
| F1023 | 10-10-10 | Summit Fertilizer | 484.17 |
| F1024 | 12-12-12 | Summit Fertilizer | 351.96 |
| F1025 | 8-32-16 | Delta Growers | 577.59 |
| F1026 | 6-24-24 | Greenfield Supply | 312.57 |
| F1027 | 12-12-12 | Summit Fertilizer | 512.66 |
| F1028 | 10-10-10 | Prairie Co-op | 393.82 |
| F1029 | 6-24-24 | Lakeshore Farms | 392.56 |
| F1030 | 28-0-0 | Delta Growers | 396.64 |
| F1031 | 6-24-24 | Greenfield Supply | 606.02 |
| F1032 | 19-19-19 | Greenfield Supply | 522.94 |
| F1033 | 15-15-15 | Greenfield Supply | 474.05 |
| F1034 | 0-0-60 | Riverbend Ag | 303.43 |
| F1035 | 10-10-10 | Hilltop Farms | 323.70 |
| F1036 | 6-24-24 | Riverbend Ag | 531.26 |
| F1037 | 0-0-60 | Summit Fertilizer | 314.90 |
| F1038 | 28-0-0 | Prairie Co-op | 554.96 |
| F1039 | 28-0-0 | Hilltop Farms | 472.22 |
| F1040 | 8-32-16 | Hilltop Farms | 476.06 |
| F1041 | 15-15-15 | Greenfield Supply | 482.83 |
| F1042 | 15-15-15 | Lakeshore Farms | 522.60 |
| F1043 | 6-24-24 | Oakridge Co-op | 485.46 |
| F1044 | 12-12-12 | Greenfield Supply | 332.40 |
| F1045 | 15-15-15 | Oakridge Co-op | 478.80 |
| F1046 | 28-0-0 | Prairie Co-op | 477.40 |
| F1047 | 6-24-24 | Riverbend Ag | 310.13 |
| F1048 | 15-15-15 | Lakeshore Farms | 419.81 |
| F1049 | 15-15-15 | Delta Growers | 536.50 |
| F1050 | 10-10-10 | Lakeshore Farms | 359.72 |

## What this means

- The costs above are **current** values; the solver may pick a different mix once D066 is more expensive.
- Formulas marked inactive are **not** included in the list.
- Binder-related formulas need a second check because the binder amount depends on the ingredient mix:
  1. Open the formula in the app.
  2. Click **Formulate** to get the new mix.
  3. Compare the new cost per ton with the one in the table.

> Never estimate a new cost by changing one number and keeping the old mix. The result looks right and is wrong.

To list them yourself, this is the query behind the answer:

```sql
SELECT p.productid, p.productname, SUM(pi.solution) AS total_solution
FROM product p
JOIN productingredient pi ON pi.productid = p.productid
WHERE pi.ingredientid = (SELECT ingredientid FROM ingredient WHERE code = 'D066')
GROUP BY p.productid, p.productname
ORDER BY p.productname;
```

Let me know if you want the change **previewed** first, or the formulas grouped by customer.
