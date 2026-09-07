Feature: Context communication — request, send, publish, complete
  IContext is the handler's authorized access to one admitted execution.
  IHandle<T> stays the capability to receive T; the runtime binds context for
  the turn. Ordinary C# decides what to send. Routing has no text predicates.

  Successful await:
    * Request — one addressed participant completed with the typed reply
    * Send — that participant handled the command
    * Publish — Bound/Innate delivery obligations are recorded; handlers are not awaited
    * Complete — the result is persisted, then visible
  Dispose releases the facade. It is not cancel and not success.
  Publishing is not completion. Exit without required complete is Incomplete.

  Rule: Request awaits a typed reply

    Scenario: A composer request returns the echo's pong with the same correlation
      Given a running brain
      And echo "echo" can handle Ping
      And composer "main" can handle Compose
      When composer "main" is asked to compose "hello"
      Then the compose reply text is "hello"
      And the ping to echo "echo" and the compose on composer "main" share a correlation
      And those two deliveries have different signal ids

  Rule: Send awaits handled; unhandled does not learn

    Scenario: A composer send to echo is handled and learns a synapse on the composer
      Given a running brain
      And echo "echo" can handle Ping
      And composer "main" can handle Compose
      When composer "main" sends ping "hello" to echo "echo"
      Then the send is handled
      And composer "main" has a learned Ping synapse to echo "echo"

    Scenario: A composer send to a neuron that cannot handle Ping fails and learns nothing
      Given a running brain
      And composer "main" can handle Compose
      When composer "main" sends ping "nobody" to composer "other"
      Then the send fails
      And composer "main" has no Ping synapse to composer "other"

  Rule: Publish records Bound recipients and does not wait

    Scenario: Publish reaches a Bound notice board and not a capable stranger
      Given a running brain
      And board "alice" can handle Notice
      And board "stranger" can handle Notice
      And board "alice" subscribes to composer "main" for Notice
      And composer "main" can handle Compose
      When composer "main" publishes notice "posted"
      Then board "alice" incoming journal contains Notice "posted"
      And board "stranger" incoming journal does not contain Notice "posted"
      And composer "main" has a bound Notice synapse to board "alice"

    Scenario: Publish is not completion of a request
      Given a running brain
      And board "alice" can handle Notice
      And board "alice" subscribes to composer "main" for Notice
      And composer "main" publishes without completing
      When composer "main" is asked to compose "orphan"
      Then the compose fails as incomplete
      And board "alice" incoming journal contains Notice "orphan"

  Rule: Child identity and completion

    Scenario: A requested echo is a child execution under the same correlation
      Given a running brain
      And echo "echo" can handle Ping
      And composer "main" can handle Compose
      When composer "main" is asked to compose "hello"
      Then the echo execution is a child of the compose execution
      And those executions share a correlation
      And the echo execution has a distinct execution id

    Scenario: Identical complete is idempotent; a conflicting complete is rejected
      Given a running brain
      And echo "echo" can handle Ping
      When echo "echo" completes ping "hello" twice with the same pong
      Then the ping reply text is "hello"
      When echo "echo" then completes ping "hello" with a different pong
      Then the conflicting complete is rejected

  Rule: Retry reuses a recorded child; a new fire does not

    Scenario: After restart, retrying the unfinished compose reuses the completed echo
      Given a running brain with durable storage
      And echo "echo" can handle Ping
      And composer "main" holds the echo result before completing
      When composer "main" is asked to compose "hello"
      And the silo restarts
      And the unfinished compose is retried
      Then the compose reply text is "hello"
      And echo "echo" handled Ping once

    Scenario: A second compose is a new execution and a new echo
      Given a running brain
      And echo "echo" can handle Ping
      And composer "main" can handle Compose
      When composer "main" is asked to compose "hello"
      And composer "main" is asked to compose "hello"
      Then echo "echo" handled Ping 2 times
