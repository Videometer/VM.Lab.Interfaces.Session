import serial
import time

# Timeout in seconds when reading command responses over the serial connection
readTimeout = 3 
# Timeout in seconds when writing commands over the serial connection
writeTimeout = 3
# Timeout when asking the VideometerLab instrument to capture and analyse an image
runRecipeTimeout = 15
# The number of times to try and connect to the VideometerLab instrument and check for the "is ready" status
initializeRetries = 3
# The time in seconds to wait in between trying to connect to the VideometerLab instrument and check for the "is ready" status
secondsToWaitBetweenInitializeRetries = 5

class VideometerLabDevice(object):
    def __init__(self):
        self.port = 'COM1'
        self.baud = 9600
        self.databits = serial.SEVENBITS
        self.parity = serial.PARITY_EVEN
        self.stopbits = serial.STOPBITS_ONE
        self.ser = None

    def Initialize(self):
        self.ser = serial.Serial(self.port,
                                 self.baud,
                                 bytesize=self.databits,
                                 parity=self.parity,
                                 stopbits=self.stopbits,
                                 timeout=readTimeout,
                                 write_timeout=writeTimeout)
        self.ser.close()
        self.ser.open()
        
        self.ser.reset_input_buffer()
        self.ser.reset_output_buffer()       
        
        nFailes = 0
        connectionOK = False
        while connectionOK == False and nFailes < initializeRetries:
            try:
                # Check the connection to the VideometerLab instrument
                self.SendCommand("CheckConnection", "ConnectionOK")

                # Check that the VideometerLab instrument is ready for the next sample
                self.SendCommand("ReadyForNextSample", "True")
                
                connectionOK = True
            except:
                print(f"Failed to connect to the VideometerLab instrument or waiting for the VideometerLab instrument to be ready for the next sample. Trying again in {secondsToWaitBetweenInitializeRetries} seconds.")
                nFailes = nFailes + 1
                time.sleep(secondsToWaitBetweenInitializeRetries)
        
        if connectionOK == False:
            raise Exception("Failed to connect to the VideometerLab instrument or waiting for the VideometerLab instrument to be ready for the next sample.")

        
    def SendCommand(self, command, expectedResult):
        try:
            self.ser.write(str.encode(command + '\n'))
        except:
            print(f"Failed to send command {command}.")
            raise Exception(f"Failed to send command {command}.")
        
        self.ser.timeout = readTimeout
        read = self.ser.readline().decode().strip()
        commandResponseOK = read == expectedResult
        
        if commandResponseOK == False:
            print(f"Failed to get expected response from command {command}. Expected {expectedResult}, but received {read}.")
            raise Exception(f"Failed to get expected response from command {command}. Expected {expectedResult}, but received {read}.")

    def RunRecipe(self, sampleId, initials, comments, suffixByTimestamp):
        commandWithParameters = f"Capture;{sampleId};{initials};{comments};{suffixByTimestamp}";
        self.SendCommand(commandWithParameters, "CaptureOK")
        
        self.ser.timeout = runRecipeTimeout
        read = self.ser.readline().decode().strip()
        
        if read != "AnalysisDone":
            raise Exception(f"Did not receive AnalysisDone within {runRecipeTimeout} seconds.")
      
