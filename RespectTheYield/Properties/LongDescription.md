# Respect The Yield

---

Respect The Yield is an extension for the Traffic mod.

## What it does

This mod forces vehicles on yield/stop/unsafe approach lanes to yield when:

- **Physical blocking**: Their next crossing lane is already occupied by another vehicle
- **Intersection priority**: Any non-yield vehicle is approaching the same intersection
- **Right-hand rule**: At uncontrolled intersections, vehicles yield to vehicles approaching from the right
- **Left-turn yield**: Vehicles turning left yield to oncoming straight moving traffic
- **Traffic lights**: At signal controlled intersections vehicles with a green signal use Right-hand rule and Left-turn yield rules
- **Unsafe lanes**: Vehicles on unsafe/non-standard lanes yield to all normal lanes

## Priority hierarchy

From lowest to highest priority: **Unsafe < Yield = Stop < Default < RightOfWay**

This means unsafe lane vehicles yield to everyone, yield/stop vehicles yield to default and right of way vehicles, etc.

## Compatibility

- Requires the **Traffic**

## Features

- **Right-hand rule enforcement**: At uncontrolled intersections, vehicles approaching from the right have priority
- **Left-turn yield**: Left-turning vehicles yield to oncoming traffic that isn't also turning
- **Configurable toggles**: Enable/disable each rule type in mod settings
- **Traffic light awareness**: Vehicles check signal state and only yield to vehicles with green signals
- **Unsafe lane detection**: Unsafe lanes are automatically flagged as lowest priority

## Known limitations

- Vehicles on different crossing lanes at the same intersection may not detect each other if both lanes are entered simultaneously
- Yield evaluation happens every frame without hysteresis
