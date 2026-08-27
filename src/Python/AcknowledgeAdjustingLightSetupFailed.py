from VideometerLabDevice import VideometerLabDevice

# This script clears a failed light setup adjustment so the VideometerLab instrument accepts commands again.
# A failed LoadLightSetup, SaveLightSetup or DoAutoLight leaves the instrument refusing further commands until
# the failure is acknowledged, so images cannot go on being captured without the light setup that never took effect.
# Run this only once the reason for the failure has been dealt with.

device = VideometerLabDevice()
try:
    device.Initialize()
    device.AcknowledgeAdjustingLightSetupFailed()
    print(f"Script complete: The failed light setup adjustment is acknowledged") # Usefull when debugging
finally:
    device.CloseComPort()
