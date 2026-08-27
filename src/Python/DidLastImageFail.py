from VideometerLabDevice import VideometerLabDevice

# This script ask the VideometerLab is the analysis of the last image failed and if so throw an exception with the error message.

device = VideometerLabDevice()
try:
    device.Initialize()
    device.CheckIfLastImageFailed()
    print(f"Script complete: Last Image Did Not Fail") # Usefull when debugging
finally:
    device.CloseComPort()
