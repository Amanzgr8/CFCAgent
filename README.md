# CFC Chatbot

AI assistant for CFC Fertilizer (and future formulation products), built per
`CFC_AI_Assistant_MVP_Architecture_and_Plan.pdf`. See that document for the
full architecture and phased plan; this file just orients the repo.

## Layout

```
src/
  CFCChatBot.McpServer/     Phase 1-2: the custom MCP server (read + write tools)
products/
  cfc-fertilizer/           Product config for CFC Fertilizer
    schema-map.yaml         Machine-readable schema map (placeholder, see file)
    schema-map.md           Human-written schema map (authoritative for now)
    instructions.md         Assistant system-prompt instructions
    test-questions.md       27-question regression test set, with answers
    docs/
      guide.md               User guide, extracted to plain text
```

A second product (e.g. a SQL-Server-backed formulation product) gets its own
`products/<name>/` folder alongside this one — see architecture doc section 2.1.

## Status

Phase 1 in progress: MCP server project scaffolded (`CFCChatBot.McpServer`,
.NET 10, official `ModelContextProtocol` SDK, stdio transport), package
versions locked (`packages.lock.json`). The generic read-tool engine is
built: it loads a product's `schema-map.yaml`, turns each `tools` entry into
a real MCP tool (parameterized SQL, no C# code needed), and registers them
with the server. `search_ingredients` and `get_ingredient` are wired up this
way for `cfc-fertilizer`. Verified end-to-end over stdio (`initialize` +
`tools/list`) without errors; not yet exercised against a live database.

Next: run it against the real CFC Fertilizer database, then add the
remaining read tools (`search_formulas`, `get_formula`, `find_formulas_using`,
`list_infeasible_formulas`, `get_customer`) the same way.

## Build

```
dotnet build
```

## Run

The server needs two things from its environment, neither of them hardcoded:

- `CFCCHATBOT_DB_CONNECTION_STRING` — an Npgsql connection string for the
  target database. Without it, the server still starts but registers no
  schema-map tools.
- `CFCCHATBOT_PRODUCT_CONFIG` — path to a product's `schema-map.yaml`.
  Defaults to `products/cfc-fertilizer/schema-map.yaml` when run via
  `dotnet run` from `src/CFCChatBot.McpServer`.

```
dotnet run --project src/CFCChatBot.McpServer
```
