# Assistant tools from the running DigitalBrain MCP server

Approved design: the built-in assistant discovers and invokes the running DigitalBrain MCP server's tools; remove duplicate in-process authoring tool definitions. Exclude send_chat_message to prevent recursive calls into the same assistant. Establish the connection lazily after kernel startup. The current MCP server is single-owner, so reject other owners/principals instead of silently using the owner identity.

1. Add real chat tests for missing routes and startup fallback. Observe RED, restore the declarative composer-to-assistant route (replies already go directly to the composer), emit durable terminal failure when neither direct recipients nor an active application connection exists, and surface terminal failures through MCP. Verify GREEN.
2. Add an assistant tool-source test against a real HTTP MCP server using real GraphTools and authoring persistence. Discover tools, save/read files, validate and activate a compiled behavior, and reject foreign contexts. Observe RED before implementation.
3. Reuse the discovered MCP session client with a local HTTP transport, register the assistant source from Aspire's endpoint configuration, and remove obsolete in-process authoring tools/tests after replacement coverage passes.
4. Run affected suites serially, restart Aspire, and verify normal UI chat, immediate missing-route failure, and tool discovery/invocation. External model responses may be deterministic; MCP, routing, authoring and persistence must be real. Record limits of live provider verification.

AppHost must be stopped during builds. Root serializes test execution; independent source work is coordinated before compiled-file tests.

Implementation: the lazy local HTTP MCP source reuses session management, discovers the canonical tools except send_chat_message, and rejects foreign owners/principals. Aspire injects the endpoint without a kernel wait dependency. Duplicate in-process authoring tools were removed. The active AgentKernel now receives the full programming instructions. Terminal route/provider failures reach the composer journal and MCP reader.

Runtime verification exposed an additional SDK mismatch: typed event ports only read a connection-bound identity, although HTTP authoring uses a verified ambient identity. The shipped-startup test was extended to both connection forms; the ambient case failed with the same live diagnostic before fixing identity resolution. Root and neuron event ports now share that resolution, including the existing foreign-principal guard.

Verification completed: Authoring 19/19, solution build with zero warnings/errors, and a real Development-mode model response using list_applications and read_application through MCP in 11.48 seconds. Saved behavior and ordinary chat responded after the final Aspire restart. See the validation record for evidence and the separate unavailable-old-worker terminal handling gap discovered during the SDK rebuild.
