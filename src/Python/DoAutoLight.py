from VideometerLabDevice import VideometerLabDevice

# This script has the VideometerLab instrument find the best light setup for the sample currently placed under
# the sphere, and applies that light setup. Have the robot present the sample before running this script,
# as the optimization measures the sample itself.
# Auto light takes considerably longer than a capture, so the timeout is correspondingly larger.
# Follow this with SaveLightSetup if the result is to be kept for later use.

device = VideometerLabDevice()
device.Initialize()
autoLightTimeoutSeconds = 30 # Unit is seconds
device.DoAutoLight(autoLightTimeoutSeconds)
print(f"Script complete: Auto light done and the found light setup is in use") # Usefull when debugging
