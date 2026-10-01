# Mermaid chart samples

Drop this file onto Whiteboard and choose Markdown. These small examples exercise
the built-in renderers; Alpha and Beta are sample labels, not required syntax.

## Pie

```mermaid
pie showData
  title Allocation
  "Alpha" : 35
  "Beta" : 65
```

## XY chart

```mermaid
xychart-beta
  title "Alpha"
  x-axis [Jan, Feb, Mar]
  y-axis "Units" 0 --> 100
  bar [30, 60, 45]
  line [20, 45, 70]
```

## Quadrant

```mermaid
quadrantChart
  title Priorities
  x-axis Low effort --> High effort
  y-axis Low value --> High value
  quadrant-1 Plan
  quadrant-2 Do
  quadrant-3 Review
  quadrant-4 Avoid
  Alpha: [0.25, 0.7]
  Beta: [0.75, 0.3]
```

## Radar

```mermaid
radar-beta
  axis speed["Speed"], quality["Quality"], cost["Cost"]
  curve a["Alpha"]{80, 90, 60}
  curve b["Beta"]{60, 70, 90}
  max 100
  min 0
```

## Sankey

```mermaid
sankey-beta
Alpha,Beta,20
Alpha,Gamma,10
Beta,Delta,20
```

## Treemap

```mermaid
treemap-beta
"Products"
  "Alpha": 30
  "Beta": 70
```

## Venn

```mermaid
venn-beta
  set A[Alpha]
  set B[Beta]
  union A,B["Shared"]
```

## Ishikawa

```mermaid
ishikawa-beta
  Alpha
  Process
    Missing review
    Incomplete test
  Equipment
    Slow connection
```
