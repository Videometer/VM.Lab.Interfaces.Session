from VideometerLabDevice import VideometerLabDevice

# This script clears a failed light setup adjustment so the VideometerLab instrument accepts commands again.
# A failed LoadLightSetup, SaveLightSetup or DoAutoLight leaves the instrument refusing further commands until
# the failure is acknowledged, so that a light setup that did not take effect cannot be captured with unnoticed.
# Run this only once the reason for the failure has been dealt with.

device = VideometerLabDevice()
device.Initialize()
device.AcknowledgeAdjustingLightSetupFailed()
print(f"Script complete: The failed light setup adjustment is acknowledged") # Usefull when debugging
