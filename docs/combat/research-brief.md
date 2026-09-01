# San9PK Combat Research Brief — Siege First

Related tracking issue: https://github.com/sappho-dev/san9-auto-domestic-research/issues/5

## Primary direction

**Siege combat is the core research target. Field battle is secondary.**

Do not spend the first pass extending generic forum-derived battle notes. The objective is to recover the executable/data model that governs attacking cities, ports, gates and fortifications, then use shared functions to fill field-combat gaps only where useful.

## First target

Find the San9PK per-tick / per-day combat execution path for a unit attacking a facility. Build a call graph around that routine and identify the two major damage channels:

1. damage to garrison troops;
2. damage to durability / walls.

For every recovered branch, record VA/RVA/file offset, constants, pseudocode, integer truncation, RNG, data-table source and confidence level.

## Siege modules to reverse engineer

### 1. Garrison damage

Recover the exact formula behind `对守兵`, including 井阑 and ordinary formations.

Determine all inputs:

- formation anti-garrison coefficient;
- troop count;
- leadership;
- morale;
- facility/garrison defensive values;
- commander/officer contribution;
- tactic contribution;
- facility type;
- RNG/clamps.

### 2. Durability damage

Recover the exact formula behind `对城壁`, especially 冲车、发石、象兵.

Verify whether values such as 40 or 20 are direct multipliers, intermediate coefficients, table indices, or inputs to a nonlinear formula.

### 3. Siege-engine behavior

Compare:

- 井阑
- 冲车
- 发石
- 象兵
- normal field formations

Check not only damage coefficients but also:

- attack frequency;
- tactic activation opportunities;
- retaliation exposure;
- target channel selection;
- hidden bonuses/penalties;
- movement/range behavior that changes actual siege throughput.

### 4. Facility retaliation

Recover how the city/port/gate/fort attacks besiegers:

- ticks per day;
- target selection among multiple attackers;
- damage formula;
- effect of garrison count and durability;
- effect of facility type;
- effect of defending officers, leadership, morale or city military values;
- behavior when garrison is near zero.

### 5. Capture trigger

Precisely determine what happens when:

- garrison reaches zero first;
- durability reaches zero first;
- both are reduced on the same tick;
- several attackers land damage around the capture tick.

Recover ownership transfer, merit/credit, prisoners, surviving troops, wounded soldiers and execution order.

### 6. Multi-unit siege

This is a strategic priority.

Determine whether splitting a large army into multiple siege units produces independent attack/tactic opportunities or hits hidden bottlenecks.

Need an executable-backed answer to:

- one 30k army vs three 10k armies;
- how many attackers can simultaneously hit one facility;
- whether each attacker gets independent ordinary/tactic ticks;
- whether retaliation scales with attacker count;
- execution order and overkill;
- target switching;
- whether surrounding affects morale or other values.

### 7. Tactics during siege

Map physical and strategy tactics into the facility-combat branches.

Questions:

- do physical tactics hit garrison only, walls only, or both?
- do facility targets use defender proficiency resistance?
- how do 井阑/弩兵 formation compatibility and tactic activation interact?
- how do 幻术、妖术、陷阱、攻心 behave against a facility/garrison?
- are status effects attached to the facility, garrison, officer set, or disabled?

### 8. Wounded soldiers and treatment

Recover the full siege casualty lifecycle:

`active troops -> casualties -> wounded -> recovery / treatment / loss / capture`

Compare attacker and garrison casualties, ordinary vs tactic damage, 方圆 conversion behavior, facility-fall behavior, and treatment behavior around ongoing sieges.

### 9. Defender geography / 地利

Verify the actual code/data definition of `地利` around owned facilities:

- range;
- ownership check;
- whether it applies to defenders inside the facility, nearby friendly units, or both;
- whether ports/gates/forts behave like cities;
- exact impact on tactic opportunity/activation.

## Secondary shared combat primitives

Recover these only where they help interpret siege branches or are naturally found in the same call graph:

- combat tick / daily execution order;
- five officer-slot effects;
- tactic opportunity generation;
- chain/联动 logic;
- abnormal-state effects;
- morale in ordinary attack/defense;
- ordinary attack formula;
- AI target selection.

## Hypothesis discipline

Old guide/forum claims are hypotheses until tied to executable or data evidence. Track each as one of:

- confirmed;
- partially confirmed;
- contradicted;
- not found.

Important hypotheses to test include:

- physical tactic power formula;
- abnormal-state ~1.3 physical-tactic vulnerability;
- physical proficiency mitigation to ~10%;
- strategy mitigation to ~30%;
- 6/9/15-day tactic intervals;
- encouragement clearing interval and high-spirited activation bonus;
- 方圆 wounded conversion;
- 鹤翼 capture bonus;
- 锥行 chain bonus;
- 箕形 sniping bonus;
- siege coefficient tables.

## Expected repository outputs

Create/update:

- `docs/combat/siege-callgraph.md`
- `docs/combat/siege-formulas.md`
- `docs/combat/combat-tables.md`
- `docs/combat/open-questions.md`
- reverse-engineering helper scripts under `tools/re/` or `prototypes/`

## Practical end state

The recovered model should be able to answer quantitatively:

- 井阑 vs 发石 vs 冲车 under different garrison/durability states;
- when killing the garrison is faster than breaking durability;
- one large siege army vs several smaller units;
- optimal siege unit size and officer composition;
- how much leadership, morale and troop count matter to siege throughput;
- how tactic-heavy compositions compare with raw troop mass.

**Siege first. Field combat is supporting evidence.**
