Feature: MCP surface
  Four tools over the same operations. An unfamiliar model must be able to use them from
  the descriptions alone.

  Scenario: The server exposes exactly fire, connect, disconnect and read
    Given a running brain
    And an MCP client for principal "claude"
    Then the tools are "connect, disconnect, fire, read"
    And every tool has a description longer than 40 characters

  Scenario: Store, group, recall through the tools
    Given a running brain
    And an MCP client for principal "claude"
    When the tool "fire" is called with {"type":"Note","body":"{\"text\":\"run tests before commit\"}","to":"run-tests"}
    And the tool "connect" is called with {"from":"git","to":"run-tests","type":"Note"}
    And the tool "read" is called with {"neuron":"git","what":"synapses"}
    Then the last tool result contains "run-tests"
    When the tool "read" is called with {"neuron":"run-tests","what":"state"}
    Then the last tool result contains "run tests before commit"

  Scenario: A rejected fire is a tool error with advice
    Given a running brain
    And an MCP client for principal "claude"
    When the tool "fire" is called with {"type":"note 1","body":"{}","to":"x"}
    Then the last tool call failed with a message containing "letters only"
