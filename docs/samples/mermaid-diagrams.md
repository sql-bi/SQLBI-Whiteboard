# Mermaid diagrams

These diagrams are rendered inside one Markdown container. Use F2 to edit the source
and Ctrl+Enter to show the result. You can draw over them with any drawing tool.

## Flowchart

```mermaid
flowchart LR
    A[Question] --> B{Needs data?}
    B -->|Yes| C[Query model]
    B -->|No| D[Answer]
    C --> D
```

| Input | Output |
| --- | --- |
| Mermaid source | A diagram you can annotate |
| Edited source | A new rendered diagram |

## Sequence diagram

```mermaid
sequenceDiagram
    participant U as User
    participant W as Whiteboard
    U->>W: Paste Markdown
    W-->>U: Show diagram
    U->>W: Draw annotations
```

## Entity relationships

```mermaid
erDiagram
    CUSTOMER ||--o{ ORDER : places
    CUSTOMER {
        int id PK
        string name
    }
    ORDER {
        int id PK
        int customerId FK
    }
```

## Multiline labels

```mermaid
flowchart TD
    A["`**Revenue** in €
    Cost and margin`"] --> B["Café 日本語"]
```
