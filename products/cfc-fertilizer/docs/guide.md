# CFC Fertilizer Formulation System - User Guide

Extracted from the original uploaded guide. Each `##` heading below is a
section served by the `search_docs`/`read_section` MCP tools.

## Getting Started

USING THE CFC FERTILIZER FORMULATION SYSTEM

Creative Formulation Concepts' Fertilizer Blending System is used to formulate a fertilizer mix based on a customer's soil test recommendations or to grade requirements. The resulting formula is the most economical ingredient mix possible based on the ingredient nutrient profiles, ingredient and nutrient formula restrictions, and ingredient costs - all of which you supply.

When using the soil test method, each formula is tailored to soil nutrient needs, desired/available ingredients, and acres (hectares) to be treated, and then optimized to these specifications. The user controls what ingredients are offered to the mix, and can specify minimums and maximums of the ingredients based on pounds/acre, pounds/ton, or percent of the mix.

When formulated to grade, the nutrient and ingredient restrictions are specified and the formula is optimized.

For both methods, a customized report is available that details all the components of the mix including, for the soil test method, the required amount and the application rate. A Batch Report suitable for plant use is also available.

Both liquid and dry mixes can be formulated.

The system can be quickly switched from US measurement units to Canadian or metric units simply by selecting that option.

It is suggested that the system be built in the following order:

Set the desired system defaults usingSystem Settings, p.2.

Review the Active Nutrient Definitions (the standard nutrients are already provided), p.6.

Add the ingredients to be used in the formulas from the Ingredient Maintenance screen, p.8. A new database does not include any ingredients. Ingredients from the previous Fertilizer program can be brought in with Import VB6 Data.

Add the customers that formulas will be prepared for, using the Customers screen.

The formulas are then created using either the Formulate Using Soil Test Requirements method or the Formulate to Grade Requirements method, pp.11-22.

If the Log in screen appears, enter your Username and Password and click Sign in.The system will open to the main menu. Following the order suggested on p.1, we will access the SystemSettingsscreen by clicking theSystem Settingsbutton.

## System Settings

The Fertilizer SystemSettingscreen is used to define the default settings for the system.The settings are saved when the screen is closed.

The ‘Your Company Name and Address’ frame identifies the name and address of the manufacturer of the fertilizer. This information will appear in the header of the formulation report. Company Name comes from your license and cannot be changed on this screen. To change it, contact CFC Tech Services, then click Refresh License Credentials on the Main Menu. Type the address, city, state, zip code, and phone number.

Select the Measurement Units as U.S. (Pounds, Tons, Acres), Canadian (Pounds, Metric Tons, Acres), or Metric (Kilograms, Metric Tons, Hectares). When the measurement unit is changed, ingredient costs and density values are converted automatically when the System Settings screen is closed. When converted to metric, all measurements are shown in metric units. In most examples, this guide refers to measurements in U.S. units.

Select the desired Dry Ingredient Cost Units: $/Ton, $/100 Lb, or $/Pound. Under Canadian or metric units these are $/Tonne, $/100 Kg, or $/Kilogram. Liquid ingredient costs are in $/Gallon (U.S.), $/Imp Gallon (Canadian), or $/Litre (metric).

Enter the number of decimals desired for the At Grade Nutrient Requirements (see p.18), separately for Dry Products and Liquid Products. From 0 to 6 decimals are allowed.

In the Product Print Options frame, select Formula Report to print only the formula report, or Formula and Batch Report to print both together. Enter the Number of Copies printed each time the Print button is clicked.

In the User Mode frame, select Single User or Multi User. With Multi User, each person signs in on the Log in screen, and the Users button appears on the Main Menu. With Single User, the program opens directly to the Main Menu without signing in.

In theFormulation Controlsframe are two tabs: Dry Products and Liquid Products. These tabs differentiate the settings necessary for formulating/reporting dry and liquid fertilizers. Dry products can only be formulated using dry ingredients, and liquid products can only be formulated using liquid ingredients.

On the Dry Products tab:

Select Yes or No to Compute and Report Approximate Bulk Density of the mix. If Yes is selected, the program calculates the approximate bulk density of each dry blend from the Bulk Density value (Lbs Per Cubic Foot) of each ingredient in Ingredient Maintenance (p.8). Enter a bulk density for every dry ingredient. An ingredient without a value is left out of the calculation, and the blend’s bulk density will not be accurate. This setting has no effect on the rest of the program or the solution.

The Dry Batch Report Options refer to the batch report created with each formulation run. The settings allow for:

the target batch size - enter the desired number.

the number of decimals to be shown on the batch report - enter the desired number of decimals.

the option to adjust the batch size for whole number of batches. If yes is selected, the batch report for a Soil Test Requirements solution will show the number of equal-amount batches that are necessary to meet the Total Requirement of the solution. The Target Batch size will never be exceeded.

several options for defining ingredient order - choose the desired option.

the option to round the ingredients in the mix when printing the batch report and the amount of the rounding.

On the Liquid Products tab:

Ingredient density values are a necessary part of liquid formulation and the density of each mix is automatically calculated and reported. All liquid ingredients must have a value in thePounds/Gallon (Lbs/Imp Gallon or Kg/Litre for Canadian or metric units)textbox in Ingredient Maintenance (p.8). Formulate requests will be denied for any liquid formula containing ingredients that do not have a density value.

The Liquid Target Batch Size measured in option directs the plant batch sheet to be reported in Pounds or Gallons (Kilograms or Imperial Gallons for Canadian units, Kilograms or Litres for metric). Choose the desired option.

The Liquid Batch Report Options refer to the batch report created with each formulation run. The settings allow for:

the target batch size - enter the desired number.

the number of decimals to be shown on the batch report - enter the desired number of decimals.

the option to adjust the batch size for whole number of batches. If yes is selected, the batch report for a Soil Test Requirements solution will show the number of equal-amount batches that are necessary to meet the Total Requirement of the solution. The Target Batch size will never be exceeded.

several options for defining ingredient order - choose the desired option.

the option to round the ingredients in the mix when printing the batch report and the amount of the rounding.

## Active Nutrient Definitions

Nutrient definitions are necessary to establish the nutrient profile of each ingredient and the fertilizer mix. Access the working screen by clicking the Active Nutrient Definitions button on the Main Menuto open the Nutrient screen.

The Nutrient screen is used to define the nutrients needed for formulation. The standard nutrients are already provided. All nutrient units are in percent. Use the Active, Inactive, and All options to choose which nutrients are listed.

To add a nutrient, type a number in Add Nutrient Number and a name in Add Nutrient Name, then click the Add Nutrient button. Each nutrient number can be used only once. A nutrient name can be changed by typing over it in the Nutrient Name column. Values for each ingredient are entered on the Ingredient Maintenance screen (p.8).

Nutrients are activated for inclusion in the formulation process by clicking a check in the Active column. Nutrients not checkedare not offeredonboth the Formulate to Soil Requirements and Formulate to Grade screens.

Total Nitrogen (nutrient 1), Phosphate (P205) (nutrient 6), and Potassium (K20) (nutrient 7) are required nutrients and must remain active.

Click the Preview button to show the nutrient list in a printable format. The list can be printed from the preview window.

## Ingredient Maintenance

The Ingredient Maintenance screen is used to add and edit the ingredients used in formulating the mixes. A new database does not include any ingredients. Add them here, or bring them in from the previous Fertilizer program with Import VB6 Data.

Access the screen from the Main Menu by selecting the Ingredient Maintenance button.

The Search Ingredient button opens the Ingredient List. Double-click the desired ingredient to show it on screen. The list can show Active, Inactive, or All ingredients. Check Binding Agents to list binding agents. The First, Previous, Next, and Last buttons can also be used to scroll through the existing ingredients.

The Nutrient Content frame is used to define the nutrient profile of the ingredient: click in the Value column on the desired nutrient and enter a value.

## Adding Ingredients

Ingredients are added to the system by clicking the Add Ingredient button. In the Add New Ingredient popup, enter the ingredient code (up to 15 characters) and, if desired, the ingredient name, and select the Ingredient Form: Dry or Liquid. Check Is a Binding Agent if the ingredient is a binder (see Binding Agents below). Dry ingredients can only be used in dry mixes, and liquid ingredients can only be used in liquid mixes. Click the Save Ingredient button on the popup, and the new ingredient is shown on screen. Enter the name, the density, the cost, and the stock status. Every liquid ingredient needs a value in the Pounds/Gallon textbox. Dry ingredients use the Bulk Density textbox, in pounds per cubic foot. The Out of Stock status prevents the ingredient from being included in the formula solution. Leave Active checked, because an inactive ingredient cannot be added to formulas. Click the Save Ingredient button to save.

## Binding Agents

If an ingredient requires a binder, select the binder in the Binding Agent box. Enter the Base amount in pounds and the Required amount of binder in pounds, or in gallons for a liquid binder. Binders are added as ingredients with Is a Binding Agent checked. They are not offered in a formula’s ingredient list. When a formula is formulated, the program adds the binders needed by the ingredients used.

## Copying Ingredients

Ingredients can also be added to the system by copying an existing ingredient. Select the ingredient to be copied and click the Save As button. Enter an ingredient code in the popup (up to eight alphanumeric digits). The screen will return with the new ingredient code but all other information, including the name, will be from the ingredient copied. Edit the data as necessary.

## Editing Ingredients

Ingredients are edited by selecting the desired ingredient and entering the new values in any textbox or nutrient row. Click the Save Ingredient button to save the changes. To change the ingredient code, click the Edit button first.

## Deactivating Ingredients

Ingredients cannot be deleted. To stop using an ingredient, clear the Active check box, or set the Stock status to Out of Stock, and click the Save Ingredient button.

## Printing Ingredients

Click the Preview button to show the ingredient’s nutrient content in a printable format. The list can be printed from the preview window. Close the preview window to return to the ingredient screen.

## Formulate Using Soil Test Requirements

A fertilizer can be formulated based on the soil requirements as determined by laboratory testing. Recommendations based on nutrient amounts (lbs/kg) per area (acre/hectare) are entered together with the size of the area to be treated, and the system returns a formula using the most economical mix of available ingredients and an application rate minimized to cost per acre (hectare).

From the Main Menu, click either the DRY Formulation to Soil Requirements or the LIQUID Formulation to Soil Requirements button to open the appropriate screen. If no formulas of that type exist yet, a message asks whether to create a new product. Click Yes, then enter a product code (up to 15 characters) and, if desired, a product name in the Add New Product popup. We will discuss the dry mix in this guide.

Nutrient Requirements tab

The Prepared For frame on the Nutrient Requirements tab is used to select the customer from the Customer list. The customer’s name, code, address, and phone number are then shown. Customers are added on the Customers screen, and clicking the Customer label opens that screen. This information appears on both the Formula Report and the Batch Report (see below).

The Field Identification frame allows entries for:

Field ID/Description for any additional information.

Number of Acres (hectares) to be treated. A number of acres must be entered before the formula can be formulated.

Lab Ref Number to cross-reference the laboratory recommendations to the formula.

Application Rate for specifying the amount of fertilizer to apply per acre/hectare.

If this box is left blank (or 0), the application rate specified by the solution will be the least amount necessary to provide the required amount per acre/hectare of each nutrient (minimize the cost per acre).

If an application rate is entered in the box that is less than the minimum required, it is generated as a minimum requirement and the solution will again return the least amount necessary to minimize the cost per acre/hectare.

If an application rate is entered that is greater than required, if necessary, it will add an ingredient to raise the total to the specified application rate, resulting in a solution that will be a greater cost per acre. If this method is used, it is suggested that a filler ingredient be included to minimize the resulting extra cost.

The Soil Requirements frame on the Nutrient Requirements tab lists the nutrients added to this formula. To add a nutrient, select it by number or name from the lists above the grid. Enter the minimum and/or maximum amount in pounds per acre (or metric units) from the customer’s soil test, and click the Add Nutrient button. The button beside the lists adds all active nutrients at once. Minimums and maximums can also be typed in the grid. It is not necessary to enter a value for each nutrient. Click the Delete button on a row to remove that nutrient from the formula.

The Description/Comments box is used for notes about the formula. These notes print on the report when Product Print Options is set to Formula and Batch Report. The Last Updated by frame shows who last saved the formula and when.

Formulation tab

The Formulation tab lists the ingredients added to this formula. To add an ingredient, select it by code or name from the lists above the grid. Enter an optional minimum, maximum, and Ingredient Unit, then click the Add Ingredient button. Only active ingredients are offered. A dry formulation screen offers only dry ingredients, and a liquid formulation screen offers only liquid ingredients. Out-of-stock ingredients are shown in blue and are not used in the solution. Remove the check in the In Stock Can Use column to leave an ingredient out of this solution, or click the Delete button on the row to remove it from the formula.

Minimum and maximum amounts of each ingredient can be specified in the appropriate columns. The Min/Max Units column allows three options for the restrictions – click in this column and, from the scroll box,select Lbs/Acre, Lbs/Ton, or % of Total(the last two options referencing the finished mix).

When all the necessary entries are complete, click the Formulate button.The formula is saved automatically before it is formulated.The solution will return and the Formulation tab will show:

In the header:

The solution Formula Status - feasible or infeasible. Infeasible results must be corrected and re-formulated (see below).

The solution Application Rate.

The Number of Acres Required as designated on the Nutrient Requirements tab.

The Total Tons required of the fertilizer to treat the designated acreage.

The Total Nitrogen (%), Phosphorus % (as P2O5); and Potassium % (as K2O) in the solution.

The Approximate Bulk Density of the finished mix. For dry mixes it is shown only when Compute and Report Approximate Bulk Density is set to Yes in System Settings.

The Cost Per Acre, Cost Per Ton, and the Total Cost of the mix based on the soil requirements, acreage, and application rate.

The table shows the ingredients offered and the amounts of each used in the solution in the Pounds Per Acre and Pounds Per Ton columns.The Cost $/Lb column lists the cost of each ingredient.

If any ingredient uses a binding agent, the binding agent grid on this tab shows the amount of each binder required.

If the nutrient and/or ingredient restrictions yield an infeasible result, the Formula Status shows Infeasible, and Infeasible prints in red on the formula report. Change the nutrient or ingredient restrictions, or offer additional ingredients, and click the Formulate button again.

Click the Preview button to open the formula report on screen, or the Print button to send it directly to the printer using the Number of Copies set in System Settings. A prompt asks whether costs should be shown on the report. Select Yes or No. The report contains the complete results of the solution. From the preview window, the report can be printed or exported. A prompt will appear asking if costs should be included on the report - select Yes or No. The report contains the complete results of the solution including a listing of all nutrients. Click the printer icon to print the report or use Copy to Clipboard or Print to File.

Click the Batch Report button to open a printable report suitable for plant use.A prompt shows the current Target Batch Size and asks whether to change it. Select Yes to enter a different batch size for this report only.The report can be instructed to show the batch sizes in equal amounts (pp. 4&5).

Formulas are identified by the Product Code and Product Name at the top of the screen. Click the Save Product button to save the formula at any time. The formula is also saved automatically each time the Formulate button is clicked. When you leave a formula with unsaved changes, a prompt asks whether to save it. The Active check box beside the product name controls whether the formula is listed as active.

Any stored formula can be recalled by clicking the Search Product button and double-clicking the formula in the Product List. The list can show Active, Inactive, or All formulas. It can be searched by product code or filtered by product name, customer name, customer code, or field description. After selecting a formula, you can edit it, click Formulate, and click Save Product. Formulas cannot be deleted. To remove a formula from the list, clear its Active check box and save it.

Formulas cannot be copied. Changing the product code of a recalled formula and saving it renames that formula. To start a new formula, click the Add Product button and enter a new product code (up to 15 characters) and, if desired, a product name in the Add New Product popup.In the popup that appears, enter a new code number and name, and click the Save button on the popup. The popup willdisappear,and the screen will show the formula copied from. Click the Get Product button to open the product index and choose the formula just created to edit and re-formulate.

## Formulate to Grade Requirements

A fertilizer can be formulated to grade specifications, that is, formulated to a specific nutrient/ingredient profile.

From the Main Menu, click either the Dry Formulation To Grade Requirements or the Liquid Formulation To Grade Requirements button to open the appropriate Formulate To Grade Requirements screen. For this guide, we will choose the dry formulation option.

Nutrient Requirements tab

The Prepared For frame is used to select the customer, as described for soil-test formulas. This information appears on both the Formula Report and the Batch Report (p.21).

The nutrient grid on the Nutrient Requirements tab is used to enter the desired nutrient profile of the fertilizer. Add nutrients one at a time, or all active nutrients at once, as described for soil-test formulas. Enter the minimum and/or maximum percent for each. It is not necessary to enter a value for each nutrient. The number of decimals is set on the System Settings screen (p.3), where 0 to 6 decimals are allowed.

Click the Formulation tab to assign ingredients to the formula.

The Field Identification frame (Dry To Grade formulation only) allows:

Field ID/Description for any additional information.

Number of Acres (hectares). A number of acres must be entered before formulating.

Lab Ref Number to cross-reference the laboratory recommendations to the formula.

Application Rate as pounds per acre (kilograms/hectare).

If entries are assigned toboththe Application Rate and Number of Acres:

the formula report (click the Preview or Print button,see p.21) will show Total Tons/Cost Required and Cost per Acre, and

the Batch Report (see p.21) will show the required number of batches based on the batch size specified on the SystemSettingsscreen (see p.4).

The Formulation tab lists the ingredients added to this formula. To add an ingredient, select it by code or name from the lists above the grid. Enter an optional minimum, maximum, and Ingredient Unit, then click the Add Ingredient button. Only active ingredients are offered. A dry formulation screen offers only dry ingredients, and a liquid formulation screen offers only liquid ingredients. Out-of-stock ingredients are shown in blue and are not used in the solution. Remove the check in the In Stock Can Use column to leave an ingredient out of this solution, or click the Delete button on the row to remove it from the formula. The Min/Max Units column offers Lbs/Acre, Lbs/Ton, and % of Total. For grade formulas, use Lbs/Ton or % of Total, which refer to the finished mix.

When entries are complete, click the Formulate button.

The solution will return and the Formulation tab will show:

In the header:

The solution Formula Status - feasible or infeasible. Infeasible results must be corrected and re-formulated (p.21).

The Total Nitrogen (%), Phosphorus % (as P2O5); and Potassium % (as K2O) in the solution.

The ApproximateDensity of the finished mix.

The Cost Per Ton of the mix.

The table shows the ingredients offered and the amounts of each used in the solutionin the Pounds Per Ton column.The Cost $/Lb column lists the cost of each ingredient.

You may wish to include a Filler ingredient when formulating fertilizers. A filler is usually an inexpensive neutral ingredient that will complete the total requirement of the formula solution while not compromising the specified nutrient profile. Fillers often allow a more economical solution to the formula.

If the nutrient and/or ingredient restrictions yield an infeasible result, the Formula Status shows Infeasible, and Infeasible prints in red on the formula report. Change the nutrient or ingredient restrictions, or offer additional ingredients, and click the Formulate button again.

Click the Preview button to open the formula report on screen, or the Print button to send it directly to the printer using the Number of Copies set in System Settings. A prompt asks whether costs should be shown on the report. Select Yes or No. The report contains the complete results of the solution. From the preview window, the report can be printed or exported.

Click the Batch Report button to open a printable report suitable for plant use.A prompt shows the current Target Batch Size and asks whether to change it. Select Yes to enter a different batch size for this report only.

Formulas are identified by the Product Code and Product Name at the top of the screen. Click the Save Product button to save the formula at any time. The formula is also saved automatically each time the Formulate button is clicked. When you leave a formula with unsaved changes, a prompt asks whether to save it. The Active check box beside the product name controls whether the formula is listed as active.

Any stored formula can be recalled by clicking the Search Product button and double-clicking the formula in the Product List. The list can show Active, Inactive, or All formulas. It can be searched by product code or filtered by product name, customer name, customer code, or field description. After selecting a formula, you can edit it, click Formulate, and click Save Product. Formulas cannot be deleted. To remove a formula from the list, clear its Active check box and save it.

Formulas cannot be copied. Changing the product code of a recalled formula and saving it renames that formula. To start a new formula, click the Add Product button and enter a new product code (up to 15 characters) and, if desired, a product name in the Add New Product popup.

## Customers

Customers are the growers or companies a formula is prepared for. Click the Customers button on the Main Menu. To add a customer, click the Add Customer button. Enter the customer code (up to 15 characters), name, address, city, state, zip code, and phone number, then click the Save Customer button. States are selected from the list on the States screen. To find a customer, click the Search Customer button and double-click the customer in the Customer List. To change a customer’s code, click the Edit button first. Customers cannot be deleted. Clear the Active check box to remove a customer from the lists used on formulas.

## States

Click the States button on the Main Menu to maintain the states used in customer addresses. Enter the State Name and State Code and click the Add State button. Click the Delete button on a row to remove a state.

## Users

The Users button appears on the Main Menu when User Mode is set to Multi User in System Settings. To add a user, enter the User Name and User Password and click the Add User button. To change a password, type it in the New Password column, and a prompt asks you to confirm the change. Clear the Active check box to prevent a user from signing in.

## License and Updates

The Main Menu shows the license type and the number of days remaining. Click Refresh License Credentials to download the latest license information from CFC Tech Services, for example after the license is renewed or the company name is changed. When a trial expires, the Trial Expired screen offers Extend 30 Days, which can be used once, or Request 1-Year License. Click Check for Update to see whether a newer version is available. If one is, save your work before continuing, because the program closes while the update is installed.

## Importing Data from the Previous Program

Nutrients, ingredients, formulas, and customers from the previous Fertilizer program can be brought into this program. Click Import VB6 Data on the Main Menu and select the folder that contains the old .DAT files, usually the Data folder. The import replaces the nutrients, ingredients, products, customers, and related data already in the program. The license, users, and system options are not changed. A summary appears when the import is complete.
