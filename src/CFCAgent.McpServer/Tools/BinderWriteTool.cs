using System.ComponentModel;
using System.Text;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Npgsql;
using ProtocolMcpServer = ModelContextProtocol.Server.McpServer;

namespace CFCAgent.McpServer.Tools;

// Hand-written "complex" write per the MVP architecture doc's complexity split: setting
// up an ingredient's binder relationship needs validation the generic schema-map-driven
// write engine can't express - the chosen binder must itself be an active ingredient
// flagged as a binding agent, or the resulting config is something the real app's own UI
// would never allow. Only touches ingredient's own binder-config columns (bindingagentid,
// ingredientbaseamount, ingredientrequiredamount) - never productbindingagent.solution,
// which is solver output recomputed by Formulate, not something this can safely write
// (same constraint as everywhere else solution-related).
//
// Built in Program.cs with the data source handed in directly, the same way the
// schema-map tools are - not discovered via WithToolsFromAssembly, and not resolved from
// the per-request service provider. That way a server process started without a DB
// connection string simply doesn't offer this tool, instead of offering it and crashing.
public sealed class BinderWriteTool(NpgsqlDataSource dataSource)
{
    [McpServerTool(Name = "set_ingredient_binder")]
    [Description("Set or clear an ingredient's binder relationship (bindingagentid, base amount, required amount), identified by its exact code. To set a binder, supply binder_code, base_amount, and required_amount together. To clear the binder, omit binder_code (or pass an empty string). ALWAYS previews first - call without confirm (or confirm=false) to see exactly what would change, show that preview to the user in your reply, then call again with the same arguments plus confirm=true - but only if the user actually asked for this change (not a what-if question). The server then asks the user to approve the exact change in a confirmation prompt, so do not ask for a separate yes in chat. Never set confirm=true on the first call.")]
    public async Task<string> SetIngredientBinder(
        ProtocolMcpServer server,
        [Description("Exact code of the ingredient to configure, as shown in the app.")] string code,
        [Description("Exact code of the binder ingredient. Must itself be an active ingredient flagged as a binding agent. Omit or pass empty to clear this ingredient's binder.")] string? binder_code = null,
        [Description("Base amount (lbs) this ingredient's binder ratio applies to. Required when setting a binder.")] double? base_amount = null,
        [Description("Required amount of binder per base amount (lbs, or gallons for a liquid binder). Required when setting a binder.")] double? required_amount = null,
        [Description("Set to true to request the change. Omit or set to false to preview it first (recommended default) - nothing is written to the database until confirm=true AND the user approves the confirmation prompt the server then shows.")] bool confirm = false,
        CancellationToken cancellationToken = default)
    {
        bool clearing = string.IsNullOrWhiteSpace(binder_code);
        if (!clearing && (base_amount is null || required_amount is null))
        {
            throw new McpException("Setting a binder requires binder_code, base_amount, and required_amount together.");
        }

        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        string? targetName;
        object? currentBinderId;
        object? currentBase;
        object? currentRequired;

        await using (NpgsqlCommand targetCommand = connection.CreateCommand())
        {
            targetCommand.Transaction = transaction;
            targetCommand.CommandText = "SELECT ingredientname, bindingagentid, ingredientbaseamount, ingredientrequiredamount FROM ingredient WHERE ingredientcode = @code";
            targetCommand.Parameters.AddWithValue("code", code);

            await using NpgsqlDataReader reader = await targetCommand.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await reader.CloseAsync();
                await transaction.RollbackAsync(cancellationToken);
                throw new McpException($"No ingredient found with code '{code}'.");
            }

            targetName = reader.IsDBNull(0) ? null : reader.GetString(0);
            currentBinderId = reader.IsDBNull(1) ? null : reader.GetValue(1);
            currentBase = reader.IsDBNull(2) ? null : reader.GetValue(2);
            currentRequired = reader.IsDBNull(3) ? null : reader.GetValue(3);
        }

        string oldBinderLabel = await ResolveBinderLabel(connection, transaction, currentBinderId, cancellationToken) ?? "(none)";

        object? newBinderId = null;
        string newBinderLabel = "(none)";

        if (!clearing)
        {
            await using NpgsqlCommand binderCommand = connection.CreateCommand();
            binderCommand.Transaction = transaction;
            binderCommand.CommandText = "SELECT ingredientid, ingredientname, COALESCE(isbindingagent, 0) <> 0 AS is_binder, COALESCE(isactive, 0) <> 0 AS is_active FROM ingredient WHERE ingredientcode = @binderCode";
            binderCommand.Parameters.AddWithValue("binderCode", binder_code!);

            await using NpgsqlDataReader reader = await binderCommand.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await reader.CloseAsync();
                await transaction.RollbackAsync(cancellationToken);
                throw new McpException($"No ingredient found with code '{binder_code}'.");
            }

            newBinderId = reader.GetValue(0);
            string binderName = reader.IsDBNull(1) ? "" : reader.GetString(1);
            bool isBinder = reader.GetBoolean(2);
            bool isActive = reader.GetBoolean(3);
            await reader.CloseAsync();

            if (!isBinder)
            {
                await transaction.RollbackAsync(cancellationToken);
                throw new McpException($"'{binder_code}' is not flagged as a binding agent. Only ingredients flagged as binding agents can be used as a binder here - flag it first with update_ingredient (is_binding_agent=true), or pick a different ingredient.");
            }

            if (!isActive)
            {
                await transaction.RollbackAsync(cancellationToken);
                throw new McpException($"'{binder_code}' is inactive. Reactivate it first, or pick a different binder.");
            }

            newBinderLabel = $"{binder_code} ({binderName})";
        }

        string subject = targetName is not null ? $"{code} ({targetName})" : code;

        StringBuilder summary = new();
        summary.AppendLine($"  binder: {oldBinderLabel} -> {newBinderLabel}");
        if (!clearing)
        {
            summary.AppendLine($"  base_amount: {FormatNumber(currentBase)} -> {base_amount}");
            summary.AppendLine($"  required_amount: {FormatNumber(currentRequired)} -> {required_amount}");
        }
        else
        {
            summary.AppendLine($"  base_amount: {FormatNumber(currentBase)} -> 0");
            summary.AppendLine($"  required_amount: {FormatNumber(currentRequired)} -> 0");
        }

        if (!confirm)
        {
            await transaction.RollbackAsync(cancellationToken);
            return $"Preview (nothing applied yet) - ingredient {subject}:\n{summary}" +
                   "Call set_ingredient_binder again with the same arguments plus confirm=true to apply this exact change.";
        }

        // confirm=true is only a request: the server asks the client to put this exact preview
        // in front of the user and writes only on an explicit yes (see WriteApproval).
        string? notApproved = await WriteApproval.RequestApprovalAsync(
            server,
            $"set_ingredient_binder: apply this change to ingredient {subject}?\n{summary}",
            cancellationToken);
        if (notApproved is not null)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            Console.Error.WriteLine($"[Audit] {DateTimeOffset.UtcNow:O} set_ingredient_binder ingredient.ingredientcode={code}: not applied - {notApproved}");
            return notApproved;
        }

        await using (NpgsqlCommand updateCommand = connection.CreateCommand())
        {
            updateCommand.Transaction = transaction;
            updateCommand.CommandText = "UPDATE ingredient SET bindingagentid = @binderId, ingredientbaseamount = @base, ingredientrequiredamount = @required WHERE ingredientcode = @code";
            // "No binder" is 0, not NULL: the CFC app maps these to non-nullable fields and tests
            // BindingAgentID > 0, and every existing ingredient stores 0 when it has no binder.
            updateCommand.Parameters.AddWithValue("binderId", newBinderId ?? 0);
            updateCommand.Parameters.AddWithValue("base", clearing ? 0d : base_amount!.Value);
            updateCommand.Parameters.AddWithValue("required", clearing ? 0d : required_amount!.Value);
            updateCommand.Parameters.AddWithValue("code", code);
            await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        Console.Error.WriteLine(
            $"[Audit] {DateTimeOffset.UtcNow:O} set_ingredient_binder ingredient.ingredientcode={code} (approved by user via elicitation): binder {oldBinderLabel} -> {newBinderLabel}");

        return $"Applied - ingredient {subject}:\n{summary}Change committed.";
    }

    private static async Task<string?> ResolveBinderLabel(
        NpgsqlConnection connection, NpgsqlTransaction transaction, object? binderId, CancellationToken cancellationToken)
    {
        if (binderId is null)
        {
            return null;
        }

        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT ingredientcode, ingredientname FROM ingredient WHERE ingredientid = @id";
        command.Parameters.AddWithValue("id", binderId);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        string code = reader.GetString(0);
        string name = reader.IsDBNull(1) ? "" : reader.GetString(1);
        return $"{code} ({name})";
    }

    private static string FormatNumber(object? value) => value is null ? "(none)" : Convert.ToString(value) ?? "(none)";
}
