# Mind maps and state diagrams

Drop this file onto the board and choose Markdown if needed. Use F2 to edit and
Ctrl+Enter to show the result. Wait for rendering before adding annotations.

## Mind map

```mermaid
mindmap
  root((DAX learning))
    Context
      Row context
      Filter context
    Measures
      Revenue
      Margin
    Practice
      Café 日本語
```

## Mind-map shapes and styled labels

```mermaid
mindmap
  root((Shapes))
    square[Square]
    rounded(Rounded)
    circle((Circle))
    bang))Bang((
    cloud)Cloud(
    hexagon{{Hexagon}}
    styled["`**Bold** and *italic*
Second line`"]
```

## Nested states and notes

```mermaid
stateDiagram-v2
  [*] --> Ready
  Ready --> Working: Start
  state Working {
    [*] --> Reading
    Reading --> Drawing: Commit
    Drawing --> [*]
  }
  Working --> Ready: Continue
  Ready --> [*]: Close
  note right of Ready
    Waiting for input
  end note
```

## Choices and parallel work

```mermaid
stateDiagram-v2
  direction LR
  state decision <<choice>>
  state split <<fork>>
  state joined <<join>>
  [*] --> decision
  decision --> split: Ready
  decision --> Finished: Skip
  split --> Validate
  split --> Preview
  Validate --> joined
  Preview --> joined
  joined --> Finished
  Finished --> [*]
  classDef completed fill:#ffeecc,stroke:#aa6600
  class Finished completed
```

## Concurrent regions

```mermaid
stateDiagram-v2
  state Active {
    [*] --> Draft
    Draft --> Committed
    --
    [*] --> Waiting
    Waiting --> Notified
  }
  [*] --> Active
  Active --> [*]
```
