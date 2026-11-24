from VideometerLabDevice import VideometerLabDevice

# This script asks the VideometerLab instrument if the analysis of the last image failed, and if so throws an exception with the error message.

device = VideometerLabDevice()
try:
    device.Initialize()
    device.CheckIfLastImageFailed()
    print(f"Script complete: Last Image Did Not Fail") # Useful when debugging
finally:
    device.CloseComPort()
