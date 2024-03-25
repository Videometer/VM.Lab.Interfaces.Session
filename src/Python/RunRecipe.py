from VideometerLabDevice import VideometerLabDevice

device = VideometerLabDevice()
device.Initialize()
device.RunRecipe("DummySampleId", "DummyInitials", "DummyComments", "True")
