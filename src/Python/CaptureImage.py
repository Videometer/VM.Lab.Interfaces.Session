from VideometerLabDevice import VideometerLabDevice

# This script return when the image is taken and sphere have moved up

device = VideometerLabDevice()
device.Initialize()
analysisTimeoutSeconds = 5 # Unit is seconds
device.WaitForAnalysisComplete(analysisTimeoutSeconds) # Wait for the previous sample analysis to complete
captureImageTimeoutSeconds = 15 # Unit is seconds
device.CaptureImage("DummySampleId", "DummyInitials", "DummyComments", "True", captureImageTimeoutSeconds)
sphereUpTimeoutSeconds = 10 # Unit is seconds
device.WaitForSphereUp(sphereUpTimeoutSeconds)
print(f"Script complete: Capture done and sphere is up") # Usefull when debugging