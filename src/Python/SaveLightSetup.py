from VideometerLabDevice import VideometerLabDevice

# This script saves the light setup currently used by the VideometerLab instrument under the given name.
# Use it after DoAutoLight to keep the optimized light setup for later use.
# If the instrument is busy capturing, an exception with the reason is thrown.

lightSetupName = "DummyLightSetup"

device = VideometerLabDevice()
try:
    device.Initialize()
    saveLightSetupTimeoutSeconds = 5 # Unit is seconds
    device.SaveLightSetup(lightSetupName, saveLightSetupTimeoutSeconds)
    print(f"Script complete: Light setup saved as {lightSetupName}") # Usefull when debugging
finally:
    device.CloseComPort()
