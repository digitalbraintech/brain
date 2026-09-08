Feature: chat
  A chat is participants (agent neurons), a transcript (the chat's incoming journal) and a
  turn policy (MAF group chat). Every Said is a real signal on a real synapse.

  Scenario: Instruct wires the anatomy
    Given a running brain with AI
    When session "claude" fires "Instruct" {"participants":["agent:writer","agent:reviewer"],"manager":"roundrobin","rounds":1} at "chat:design"
    And "chat:design" waits up to 10 seconds for a synapse to "agent:reviewer" for "Turn"
    Then "chat:design" has a synapse to "agent:writer" for "Turn"
    And "chat:design" has a synapse to "agent:reviewer" for "Turn"
    And "agent:writer" has a synapse to "chat:design" for "Said"
    And "agent:reviewer" has a synapse to "chat:design" for "Said"

  Scenario: Two participants take turns and the asker gets one Reply
    Given a running brain with AI
    And the scripted model will say "draft: brain"
    And the scripted model will say "review: ship it"
    When session "claude" fires "Instruct" {"participants":["agent:writer","agent:reviewer"],"manager":"roundrobin","rounds":1} at "chat:design"
    And session "claude" fires "Ask" {"text":"a slogan"} at "chat:design"
    And "claude" waits up to 20 seconds for an incoming "Reply"
    Then "chat:design" incoming "Said" bodies are {"author":"agent:writer","text":"draft: brain"}, {"author":"agent:reviewer","text":"review: ship it"}
    And "claude" incoming journal has 1 entries
    And the latest "claude" incoming "Reply" text contains "ship it"

  Scenario: The transcript is readable with no participant alive
    Given a running brain with durable storage and AI
    And the scripted model will say "one"
    And the scripted model will say "two"
    When session "claude" fires "Instruct" {"participants":["agent:writer","agent:reviewer"],"manager":"roundrobin","rounds":1} at "chat:design"
    And session "claude" fires "Ask" {"text":"go"} at "chat:design"
    And "claude" waits up to 20 seconds for an incoming "Reply"
    And the silo restarts
    Then "chat:design" incoming journal has 4 entries

  Scenario: A chat resumes after a restart mid-conversation
    Given a running brain with durable storage and AI
    And the scripted model will say "one"
    And the scripted model will pause before its next answer
    And the scripted model will say "two"
    When session "claude" fires "Instruct" {"participants":["agent:writer","agent:reviewer"],"manager":"roundrobin","rounds":1} at "chat:design"
    And session "claude" fires "Ask" {"text":"go"} at "chat:design"
    And "chat:design" waits up to 10 seconds for an incoming "Said"
    And the silo restarts
    And the scripted model is unpaused
    And "chat:design" waits up to 10 seconds for an incoming "Said"
    And "agent:reviewer" waits up to 10 seconds for an incoming "Turn"
    And "claude" waits up to 30 seconds for an incoming "Reply"
    Then "chat:design" incoming "Said" bodies are {"author":"agent:writer","text":"one"}, {"author":"agent:reviewer","text":"two"}

  Scenario: Ask before Instruct explains what to do
    Given a running brain with AI
    When session "claude" fires "Ask" {"text":"hi"} at "chat:empty"
    And "claude" waits up to 5 seconds for an incoming "Reply"
    Then the latest "claude" incoming "Reply" text contains "Instruct this chat with at least two participants"
