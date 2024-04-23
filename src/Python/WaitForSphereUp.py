from VideometerLabDevice import VideometerLabDevice

# This script retrun when the sphere have moved up
# Use this script to ensure the sphere have moved up before having the robot present the first sample under the VideometerLab sphere

device = VideometerLabDevice()
device.Initialize()
sphereUpTimeoutSeconds = 15 # Unit is seconds
device.WaitForSphereUp(sphereUpTimeoutSeconds)
print(f"Script complete: Sphere is up") # Usefull when debugging