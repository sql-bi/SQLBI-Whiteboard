# Mermaid structure samples

Drop this file onto Whiteboard and choose Markdown. The final four examples are
different notations for one family: railroad diagrams.

## Class

```mermaid
classDiagram
  class Alpha {
    +string Name
    +Validate() bool
  }
  Alpha "1" --> "many" Beta : creates
```

## Block

```mermaid
block-beta
  columns 3
  a["Alpha"] space b["Beta"]
  a --> b
```

## Architecture

```mermaid
architecture-beta
  group app(cloud)[Application]
  service a(server)[Alpha] in app
  service b(database)[Beta] in app
  a:R --> L:b
```

## C4

```mermaid
C4Context
  Person(a, "Alpha", "Analyst")
  System(b, "Beta", "Reporting")
  Rel(a, b, "Uses")
```

## Use case

```mermaid
usecase-beta
  direction LR
  actor Analyst("Alpha")
  systemBoundary "Application"
    Review("Beta")
  end
  Analyst --> Review
```

## Requirement

```mermaid
requirementDiagram
  requirement Alpha {
    id: 1
    text: Retain source
    risk: low
    verifymethod: test
  }
  element Beta {
    type: application
  }
  Beta - satisfies -> Alpha
```

## Git graph

```mermaid
gitGraph
  commit id: "Alpha"
  branch feature
  checkout feature
  commit id: "Beta"
  checkout main
  merge feature
```

## Packet

```mermaid
packet-beta
  0-7: "Alpha"
  8-15: "Beta"
  16-31: "Payload"
```

## Tree view

```mermaid
treeView-beta
├── Alpha/
│   ├── one.txt
│   └── two.txt
└── Beta.txt
```

## Railroad

```mermaid
railroad-beta
entry = sequence(terminal("Alpha"), optional(nonterminal("tail"))) ;
tail = choice(terminal("Beta"), terminal("Gamma")) ;
```

## Railroad EBNF

```mermaid
railroad-ebnf-beta
entry = "Alpha" [ tail ] ;
tail = "Beta" | "Gamma" ;
```

## Railroad ABNF

```mermaid
railroad-abnf-beta
entry = "Alpha" [ tail ] ;
tail = "Beta" / "Gamma" ;
```

## Railroad PEG

```mermaid
railroad-peg-beta
entry <- "Alpha" tail? ;
tail <- "Beta" / "Gamma" ;
```
