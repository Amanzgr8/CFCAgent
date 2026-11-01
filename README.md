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
.NET 8, official `ModelContextProtocol` SDK, stdio transport), builds clean,
smoke-test `ping` tool in place. Next: design the generic read-tool engine
that consumes `schema-map.yaml`, then implement the read tools validated in
the POC.

## Build

```
dotnet build
```
