from VideometerLabDevice import VideometerLabDevice

# This script starts a new measurement on the VideometerLab instrument: the results shown are cleared and the
# instrument is made ready for a fresh analysis, with the results saved to new files.
# Run this before the first sample of a new batch, so the batch does not append to the previous one's files.

device = VideometerLabDevice()
try:
    device.Initialize()
    device.New()
    print(f"Script complete: A new measurement is started") # Usefull when debugging
finally:
    device.CloseComPort()
