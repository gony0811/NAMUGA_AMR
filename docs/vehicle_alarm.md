
# AMR Alarm List
```
## Template
### Alarm Id
### Alarm Code
### Alarm Name
### Condition
1. Condition 1
2. Condition 2
### Description
- Description of the alarm
```
------------------------------------------------------------------------

## ERR-100
### ACS Alarm Code: 100
### Cobot Not Ready
### Condition
1. Modbus Disconnect
2. Cobot Disable
3. Main Program Stop
4. Manual Mode

### Description
- Cobot is not ready to perform transport task


## AMR-ERR-101
### ACS Alarm Code: 101
### AMR Not Ready
### Condition
1. Modbus Disconnect
### Description
- AMR is not ready to perform transport task


## AMR-ERR-102
### ACS Alarm Code: 102
### Camera does not ready
### Condition
1. Camera Disconnect
### Description
- Camera does not ready to perform a transport task

## AMR-ERR-103
### ACS Alarm Code: 103
### QR Code reading failure
### Condition
1. QR Code reading failure
2. QR Code reading position data is not valid (x = 0, y = 0, theta = 0)
### Description
- QR Code reading failure or QR Code reading position data is not valid (x = 0, y = 0, theta = 0)
- AMR transferState가 ASSIGNED 또는 TRANSFERING_SOURCE 인 경우, Assign된 TransportCommand를 QUEUED상태 할당된 AMR ID도 empty로 rollback 시킴
- AMR도 ProcessingState를 IDLE로 변경하고 TransferState도 NOTASSIGNED로 변경함
- 만약 AMR transferState가 TRANSFERING_DEST 인 경우, 아무런 조치도 하지 않음


