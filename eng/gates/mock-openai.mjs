#!/usr/bin/env node
// A dependency-free OpenAI-compatible mock server for gate C (eng/gates/crush-gate.sh).
//
// Serves GET /v1/models (one model, "mock") and POST /v1/chat/completions on the port given as
// argv[2], appending each request's raw body to the log file given as argv[3]. The answer depends
// only on whether the conversation already contains a `tool`-role message:
//   - no tool message yet: one tool call to the "bash" tool with arguments
//     {"command":"dotnet build","description":"build"}, so Crush asks the agent to build the project.
//   - a tool message is present: a plain assistant message "done", ending the turn.
// Both `stream: false` (a single JSON object) and `stream: true` (OpenAI-format SSE chunks, ending
// with `data: [DONE]`) are supported, because Crush may request either.
//
// Usage: node mock-openai.mjs <port> <requestLogFile>

import http from "node:http";
import fs from "node:fs";

const port = Number(process.argv[2]);
const requestLogFile = process.argv[3];

if (!Number.isInteger(port) || !requestLogFile) {
    console.error("usage: mock-openai.mjs <port> <requestLogFile>");
    process.exit(1);
}

const MODEL_ID = "mock";

/** Finds the "bash" tool in the request's `tools` array; throws when none is present. */
function findBashTool(tools) {
    const found = (tools ?? []).find((tool) => tool?.function?.name === "bash");
    if (!found) {
        throw new Error('mock-openai: no tool named "bash" in the request\'s tools array');
    }
    return found;
}

/** Builds the `bash` tool call arguments from the schema's own property names. */
function buildBashArguments(bashTool) {
    const properties = bashTool.function?.parameters?.properties ?? {};
    if (!("command" in properties)) {
        throw new Error('mock-openai: the "bash" tool has no "command" parameter');
    }

    const args = { command: "dotnet build" };
    if ("description" in properties) {
        args.description = "build";
    }
    return args;
}

/** True when any message in the conversation already has role "tool" (the build already ran). */
function hasToolResult(messages) {
    return (messages ?? []).some((message) => message?.role === "tool");
}

let completionCount = 0;

/** A completion id unique within this server's lifetime; a counter, since nothing needs it unpredictable. */
function chatCompletionId() {
    completionCount += 1;
    return `chatcmpl-mock-${Date.now()}-${completionCount}`;
}

/** Builds the non-streaming `chat.completion` response body for either turn shape. */
function buildCompletion(requestBody) {
    const created = Math.floor(Date.now() / 1000);
    const id = chatCompletionId();

    if (hasToolResult(requestBody.messages)) {
        return {
            id,
            object: "chat.completion",
            created,
            model: MODEL_ID,
            choices: [
                {
                    index: 0,
                    message: { role: "assistant", content: "done" },
                    finish_reason: "stop",
                },
            ],
            usage: { prompt_tokens: 0, completion_tokens: 0, total_tokens: 0 },
        };
    }

    const bashTool = findBashTool(requestBody.tools);
    const args = buildBashArguments(bashTool);
    return {
        id,
        object: "chat.completion",
        created,
        model: MODEL_ID,
        choices: [
            {
                index: 0,
                message: {
                    role: "assistant",
                    content: null,
                    tool_calls: [
                        {
                            id: "call_mock_1",
                            type: "function",
                            function: { name: "bash", arguments: JSON.stringify(args) },
                        },
                    ],
                },
                finish_reason: "tool_calls",
            },
        ],
        usage: { prompt_tokens: 0, completion_tokens: 0, total_tokens: 0 },
    };
}

/** Writes one SSE `data:` line for a chat-completion chunk. */
function writeChunk(res, chunk) {
    res.write(`data: ${JSON.stringify(chunk)}\n\n`);
}

/** Streams the plain "done" answer as OpenAI-format SSE chunks. */
function streamDone(res, id, created) {
    writeChunk(res, {
        id,
        object: "chat.completion.chunk",
        created,
        model: MODEL_ID,
        choices: [{ index: 0, delta: { role: "assistant" }, finish_reason: null }],
    });
    writeChunk(res, {
        id,
        object: "chat.completion.chunk",
        created,
        model: MODEL_ID,
        choices: [{ index: 0, delta: { content: "done" }, finish_reason: null }],
    });
    writeChunk(res, {
        id,
        object: "chat.completion.chunk",
        created,
        model: MODEL_ID,
        choices: [{ index: 0, delta: {}, finish_reason: "stop" }],
    });
    res.write("data: [DONE]\n\n");
    res.end();
}

/** Streams the "bash" tool-call answer as OpenAI-format SSE chunks. */
function streamToolCall(res, id, created, args) {
    writeChunk(res, {
        id,
        object: "chat.completion.chunk",
        created,
        model: MODEL_ID,
        choices: [
            {
                index: 0,
                delta: {
                    role: "assistant",
                    tool_calls: [{ index: 0, id: "call_mock_1", type: "function", function: { name: "bash", arguments: "" } }],
                },
                finish_reason: null,
            },
        ],
    });
    writeChunk(res, {
        id,
        object: "chat.completion.chunk",
        created,
        model: MODEL_ID,
        choices: [
            {
                index: 0,
                delta: { tool_calls: [{ index: 0, function: { arguments: JSON.stringify(args) } }] },
                finish_reason: null,
            },
        ],
    });
    writeChunk(res, {
        id,
        object: "chat.completion.chunk",
        created,
        model: MODEL_ID,
        choices: [{ index: 0, delta: {}, finish_reason: "tool_calls" }],
    });
    res.write("data: [DONE]\n\n");
    res.end();
}

function handleChatCompletions(requestBody, res) {
    const created = Math.floor(Date.now() / 1000);
    const id = chatCompletionId();
    const stream = requestBody.stream === true;

    if (hasToolResult(requestBody.messages)) {
        if (stream) {
            res.writeHead(200, { "Content-Type": "text/event-stream" });
            streamDone(res, id, created);
        } else {
            res.writeHead(200, { "Content-Type": "application/json" });
            res.end(JSON.stringify(buildCompletion(requestBody)));
        }
        return;
    }

    const bashTool = findBashTool(requestBody.tools);
    const args = buildBashArguments(bashTool);

    if (stream) {
        res.writeHead(200, { "Content-Type": "text/event-stream" });
        streamToolCall(res, id, created, args);
    } else {
        res.writeHead(200, { "Content-Type": "application/json" });
        res.end(JSON.stringify(buildCompletion(requestBody)));
    }
}

function handleModels(res) {
    res.writeHead(200, { "Content-Type": "application/json" });
    res.end(
        JSON.stringify({
            object: "list",
            data: [{ id: MODEL_ID, object: "model", created: 0, owned_by: "mock" }],
        }),
    );
}

const server = http.createServer((req, res) => {
    const chunks = [];
    req.on("data", (chunk) => chunks.push(chunk));
    req.on("end", () => {
        const rawBody = Buffer.concat(chunks).toString("utf8");
        try {
            fs.appendFileSync(requestLogFile, `${req.method} ${req.url}\n${rawBody}\n\n`);
        } catch (logError) {
            console.error(`mock-openai: failed to write request log: ${logError.message}`);
        }

        try {
            if (req.method === "GET" && req.url?.startsWith("/v1/models")) {
                handleModels(res);
                return;
            }

            if (req.method === "POST" && req.url?.startsWith("/v1/chat/completions")) {
                const requestBody = rawBody.length > 0 ? JSON.parse(rawBody) : {};
                handleChatCompletions(requestBody, res);
                return;
            }

            res.writeHead(404, { "Content-Type": "application/json" });
            res.end(JSON.stringify({ error: `mock-openai: no route for ${req.method} ${req.url}` }));
        } catch (error) {
            const message = `mock-openai: ${error.message}`;
            console.error(message);
            res.writeHead(500, { "Content-Type": "application/json" });
            res.end(JSON.stringify({ error: { message } }));
        }
    });
});

server.listen(port, "127.0.0.1", () => {
    console.log(`mock-openai: listening on http://127.0.0.1:${port}/v1`);
});
