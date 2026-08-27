from VideometerLabDevice import VideometerLabDevice

# This script loads a named light setup into the VideometerLab instrument and returns when it is in use.
# A light setup with the given name must already exist. If it does not, or if the instrument is busy capturing,
# an exception with the reason is thrown.

lightSetupName = "DummyLightSetup"

device = VideometerLabDevice()
try:
    device.Initialize()
    loadLightSetupTimeoutSeconds = 5 # Unit is seconds
    device.LoadLightSetup(lightSetupName, loadLightSetupTimeoutSeconds)
    print(f"Script complete: Light setup {lightSetupName} is loaded") # Usefull when debugging
finally:
    device.CloseComPort()
