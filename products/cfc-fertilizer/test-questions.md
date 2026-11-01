# CFC Assistant — Test Question Set (Step 6)

27 questions across three groups, with the correct answer written down for each, so the
assistant's actual answers can be scored (Correct / Partly correct / Wrong) against them.

Unless noted, the "correct answer" below was verified by running the query live against
the `fertilizer` database through the `cfc-postgres` read-only MCP connection just now, or
by reading the exact wording in the uploaded user guide. **Data-lookup answers are a
snapshot** — if the database changes, re-run the query behind the question before scoring.

## Known data caveat to watch for while scoring

`product.formulationstatus` does **not** reliably mean "has no solution" when it's `1`.
Confirmed in the VB6 import code: every migrated formula gets its status reset to `1`
(NotFormulated) regardless of whether it already had a computed solution in the old
program — only a formula that's been run through *this* app's Formulate button since
import gets `2` (Feasible) or `3` (Infeasible). In this database, ~492 of 498 formulas
sit at status `1` while still carrying real `solution` amounts from the old system. If the
assistant says "this formula hasn't been formulated" based on status `1` alone, that's a
plausible-sounding but often wrong answer — it should check whether `solution` values
exist too, or say the status is unverified since migration.

---

## A. Data lookups (10)

**A1. How many ingredients are in the system, and how many are currently out of stock?**
Correct answer: 33 ingredients, all 33 active. 12 are out of stock: 5 dry (`D010` Ammonium
Nitrate, `D463` Potassium Sulfate, `D716` Copper Oxide, `D734` Ferrous Sulfate, `D778` Zinc
Oxide) and 7 of the 8 liquid ingredients (`L006`, `L012`, `L031`, `L207`, `L248`, `L249`,
`L423`) — only `L000` Water is a liquid ingredient currently in stock.

**A2. Which liquid ingredients are currently missing their required density (Lbs/Gallon) value?**
Correct answer: none right now — every active liquid ingredient has a non-zero
`poundpergallon` value, so no liquid formula is currently blocked for this reason.

**A3. What's the nutrient profile of Treated Urea (`D066`)?**
Correct answer: only one nutrient is recorded for it — 46% under **Nitrate Nitrogen**
(nutrient #2). Its Total Nitrogen (#1) value is 0/blank. (This is a trap: don't assume
urea's nitrogen is filed under nutrient #1 just because that's "Total Nitrogen" — check
the actual row. Worth flagging to a product expert as a possible data-entry
inconsistency — see C5.)

**A4. Which active formulas currently use Treated Urea (`D066`) in their solution, and how many are there?**
Correct answer: 55 active formulas. A few examples: `008` Rolling Wheels Farms top dress
(60 lbs/acre), `4008` KT Farms 45-0-0-10 (79 lbs/acre), `556` hegarty urea estes hill (353
lbs/acre — the largest user).

**A5. List all of "KT Farms"'s formulas.**
Correct answer: there are **two separate customer records** that both look like KT Farms —
`KTFARMS` ("KT Farms", 21 formulas) and `KTFARMS2` ("K T Farms", 4 formulas), 25 formulas
total. An answer that only searches one of them is incomplete. (See C4 — this looks like a
duplicate customer record, not something to silently resolve.)

**A6. What are the 5 cheapest active, in-stock dry ingredients by cost?**
Correct answer: Magnesium Oxide (`D443`, $0/lb), Oats (`OATS`, $0/lb), Pel-lime (`D429`,
$0.095/lb), Gypsum (`D000`, $0.1325/lb), Gypsum w/Zinc (`D803`, $0.135/lb). Flag the two
$0/lb entries as likely missing cost data rather than stating they're genuinely free.

**A7. Are there any binding agents set up right now?**
Correct answer: no. Zero ingredients are flagged as binding agents, and the
`productbindingagent` table has zero rows system-wide.

**A8. What system settings is this installation using — measurement units, cost unit, user mode?**
Correct answer: US units (not Canadian or metric), dry ingredient cost unit = $/Ton,
Multi-User mode, "Compute and Report Approximate Bulk Density" is on, default dry batch
size 19,000 lbs, default liquid batch size 500 gal.

**A9. Which formula(s) are currently marked Feasible, and which are marked Infeasible?**
Correct answer: exactly one of each. Feasible: `008` "Rolling Wheels Farms top dress"
(customer Rolling Wheels farms). Infeasible: `757` (customer Landon Jeschke). See the data
caveat above — this doesn't mean every other formula is "not formulated," just that only
these two have been re-run through Formulate since the VB6 import.

**A10. Are there any liquid formulas in the system? What are they?**
Correct answer: two, both look like built-in templates rather than real customer
formulas — `L1` "Liquid Soil Test Formula" and `L2` "Liquid Blend to Grade Requirements,"
both currently at status NotFormulated.

---

## B. How-to (9) — answers from the uploaded user guide

**B1. How do I add a new ingredient?**
Ingredient Maintenance screen → Add Ingredient → enter the code (up to 15 characters),
optionally a name, pick Dry or Liquid, check "Is a Binding Agent" if it's a binder → Save
Ingredient on the popup → then fill in name, density (Bulk Density for dry, Pounds/Gallon
for liquid), cost, and stock status → Save Ingredient again.

**B2. Why would a liquid formula get rejected when I click Formulate?**
Because one or more of its liquid ingredients is missing a Pounds/Gallon density value —
every liquid ingredient must have one, or Formulate is denied for that formula.

**B3. How do I stop using an ingredient without deleting it?**
Nothing in CFC can be deleted. Clear its Active checkbox, or set its stock status to Out
of Stock, then click Save Ingredient.

**B4. How do I set up a binder for an ingredient?**
Add the binder itself as its own ingredient with "Is a Binding Agent" checked (binders
aren't offered directly in a formula's ingredient list). Then, on the ingredient that
needs it, pick that binder in the Binding Agent box and enter the Base Amount and
Required Amount (lbs, or gallons if the binder is liquid). CFC adds the binder
automatically whenever that ingredient is used in a formula.

**B5. What happens when Formulate comes back Infeasible?**
The Formula Status shows Infeasible and prints in red on the report; no usable solution
is produced. You change the nutrient or ingredient restrictions, or offer more
ingredients, and click Formulate again.

**B6. What's the difference between "Formulate Using Soil Test Requirements" and "Formulate to Grade Requirements"?**
Soil test: nutrient min/max are lbs/acre (from a soil test), tied to a number of acres and
an optional application rate, optimized for lowest cost per acre. Grade: nutrient min/max
are percent of the finished mix (a target grade), optimized for lowest cost per ton —
acres are only needed if you also want cost-per-acre reporting.

**B7. Can I copy an existing formula to start a new one?**
No — formulas can't be copied (only ingredients can, via Save As). To start a new
formula, use Add Product and enter a new code. Changing the code of a recalled formula
and saving it renames that formula instead of copying it.

**B8. How do I switch the whole system to metric units, and what happens to existing ingredient costs?**
System Settings → Measurement Units → select Metric. Ingredient costs and density values
convert automatically when the System Settings screen is closed; settings are saved at
that point too.

**B9. A customer's name is wrong — can I delete that customer record and re-add it?**
No — customers can't be deleted either, only deactivated (clear the Active checkbox).
Correct the name on the existing record instead.

---

## C. Analysis (8)

**C1. What's the simplest way to make a formula infeasible?**
Set a nutrient minimum higher than any feasible combination of in-stock ingredients can
reach. Real example in this database: formula `757` requires a 90% minimum Total Nitrogen
in the finished mix, but the highest-nitrogen ingredient available anywhere in the system
tops out at 34% (`D010` Ammonium Nitrate — which is itself out of stock) — no mix can
reach 90%, so it's Infeasible. A second way (not currently present in this data) is
requiring a minimum amount of an ingredient that's out of stock.

**C2. If Treated Urea's cost went up $0.05/lb, which formulas would be affected, and what would the new cost per acre be?**
Correct answer: the assistant can correctly list the 55 affected formulas (see A4), but
**cannot** compute a new cost per acre — that requires re-running the optimizer, which
this proof of concept doesn't have access to. A correct answer names the affected
formulas and tells the user to update the cost in Ingredient Maintenance and click
Formulate (or wait for a reformulation tool) to see the new numbers. An answer that
invents a new cost-per-acre number is wrong, no matter how plausible it looks.

**C3. Which formulas might be giving a stale answer right now (an ingredient changed after the formula was last formulated)?**
Correct answer: none, as of this check — comparing every active formula's
`productingredient` ingredients' `updatestamp` against the formula's own `updatestamp`
returns zero rows where the ingredient is newer.

**C4. Is "KT Farms"'s formula list complete if you only search customer code `KTFARMS`?**
Correct answer: no — there's a second, separate customer record (`KTFARMS2` / "K T Farms")
with 4 more formulas not under `KTFARMS`. This looks like a duplicate customer entry the
product team may want to merge; the assistant should surface both rather than silently
picking one or merging them on its own.

**C5. Why does Treated Urea's nutrient analysis show 0% Total Nitrogen?**
Correct answer: because its 46% nitrogen is recorded under the "Nitrate Nitrogen"
(nutrient #2) row instead of "Total Nitrogen" (#1) — not because urea has no nitrogen.
Worth flagging to a product expert, since agronomically urea's nitrogen is usually
tracked as Total N; this may be a data-entry inconsistency worth fixing at the source.

**C6. Could any formula have a binder added to it today?**
Correct answer: no — there are currently zero ingredients anywhere in the system flagged
as binding agents, so no formula can pick up a binder right now regardless of what's in
its ingredient list.

**C7. Is any formula currently blocked from formulating because a required ingredient is out of stock?**
Correct answer: not right now — there's no `productingredient` row with a minimum greater
than 0 on an ingredient that's also out of stock. (A good trap question: the assistant
should check this directly rather than assuming it from the guide's general rule.)

**C8. Besides re-running the optimizer, what can this assistant *not* do for formula `757` (the infeasible one)?**
Correct answer: it can't lower the 90% nitrogen minimum, add or swap ingredients, or click
Formulate for the user — all of that happens in the CFC application itself. In this
read-only proof of concept, the assistant can only diagnose the cause (see C1) and tell
the user what to change.

---

## Suggested follow-up (not part of this question set)

The `formulationstatus` caveat above (migrated formulas keep real `solution` data but get
reset to status `1`) and the duplicate "KT Farms" customer record are both things worth
adding as a ground rule to `CFC_Schema_Map.md` and/or raising with whoever owns the data,
since they'll trip up both the AI and a human relying on `formulationstatus` or a single
customer-code lookup at face value.
