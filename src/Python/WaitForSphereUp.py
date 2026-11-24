from VideometerLabDevice import VideometerLabDevice

# This script returns when the sphere has moved up
# Use this script to ensure the sphere has moved up before having the robot present the first sample under the VideometerLab sphere

device = VideometerLabDevice()
try:
    device.Initialize()
    sphereUpTimeoutSeconds = 15 # Unit is seconds
    device.WaitForSphereUp(sphereUpTimeoutSeconds)
    print(f"Script complete: Sphere is up") # Useful when debugging
finally:
    device.CloseComPort()
