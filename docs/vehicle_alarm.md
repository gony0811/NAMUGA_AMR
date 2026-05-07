# AMR Alarm List

## ERR-100
### Cobot Not Ready
### Severity (Level: Warning | Critical)
- Critical
### Condition
1. Modbus Disconnect
2. Cobot Disable
3. Main Program Stop
4. Manual Mode
### Description
- Cobot is not ready to perform a transport task

## ERR-101
### AMR Not Ready
### Severity (Level: Warning | Critical)
- Critical
### Condition
1. Modbus Disconnect
### Description
- AMR is not ready to perform a transport task

## ERR-102
### Camera Doesn’t Ready
### Severity (Level: Warning | Critical)
- Critical
### Condition
1. Camera Disconnect
### Description
- Camera is not ready.

## ERR-103
### Cobot Collision Error
### Severity (Level: Warning | Critical)
- Critical
### Condition
1. Cobot Collision Detected
### Description
- Cobot has detected a collision. Please check the surroundings of the AMR and clear any obstacles.

## ERR-104
### AMR Map Matching Error
### Severity (Level: Warning | Critical)
- Critical
### Condition
1. Map Matching Rate < 30%
### Description
- AMR is having difficulty matching its current position to the map. Please check the environment and ensure that the AMR can see enough landmarks for localization.

## ERR-105
### AMR Magazine Unloaded by manually
### Severity (Level: Warning | Critical)
- Critical
### Condition
1. AMR Processing State is Run
2. AMR Transfer State is TRANSFERING_DEST
3. the trigger is when AMR Port Sensor On -> Off
4. 1, 2, 3 Condition is all true

### Description
- AMR is having difficulty matching its current position to the map. Please check the environment and ensure that the AMR can see enough landmarks for localization.
