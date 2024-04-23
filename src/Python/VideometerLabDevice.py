import serial
import time

# Timeout in seconds when reading command responses over the serial connection
defaultReadTimeout = 3 
# Timeout in seconds when writing commands over the serial connection
defaultWriteTimeout = 3
# The number of times to try and connect to the VideometerLab instrument and check for the "is ready" status
initializeRetries = 3
# The time in seconds to wait in between trying to connect to the VideometerLab instrument and check for the "is ready" status
secondsToWaitBetweenInitializeRetries = 5
# The maximum number of times ot try and resend a command if the sending of the command fails or if the response is not the expected.
sendCommandMaxRetries = 3

class VideometerLabDevice(object):
    def __init__(self):
        self.port = 'COM1' # Move to parameter in the top of the file
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
                                 timeout=defaultReadTimeout,
                                 write_timeout=defaultWriteTimeout)
        self.ser.close()
        self.ser.open()
        
        self.ser.reset_input_buffer()
        self.ser.reset_output_buffer()       
        
        nFailes = 0
        while nFailes < initializeRetries:
            try:
                # Check the connection to the VideometerLab instrument
                self.SendCommandWithRetry("CheckConnection", "ConnectionOK", defaultReadTimeout)
                return          
            except:
                print(f"Failed to connect to the VideometerLab instrument. Trying again in {secondsToWaitBetweenInitializeRetries} seconds.")
                nFailes = nFailes + 1
                time.sleep(secondsToWaitBetweenInitializeRetries)
        
        raise Exception("Failed to connect to the VideometerLab instrument.")
        
    def SendCommand(self, command, expectedResult, readTimeout):
        try:
            self.ser.write(str.encode(command + '\n'))
        except:
            print(f"Failed to send command {command}.")
            raise Exception(f"Failed to send command {command}.")
        
        self.ser.timeout = readTimeout
        read = self.ser.readline().decode().strip()
        commandResponseOK = read == expectedResult
        
        if commandResponseOK == False:
            error_message = f"Failed to get expected response from command {command}. Expected {expectedResult}, but received {read}."
            error_message_joined = ''.join(error_message)
            print(error_message_joined)
            error_message_string = ''.join(map(str, error_message))
            raise Exception("{}".format(error_message))

    def SendCommandWithRetry(self, command, expectedResult, readTimeout, maxAttempts=sendCommandMaxRetries):
        attempt = 0
        while attempt < maxAttempts:
            try:
                self.SendCommand(command, expectedResult, readTimeout)
                return  # Command succeeded, no need to retry
            except Exception as e:
                attempt += 1
                if attempt < maxAttempts:
                    print(f"Retrying command {command}")
                else:
                    print(f"Maximum attempts reached. Giving up on command {command}.")
                    e.args = f"Maximum attempts reached. Giving up on command {command}. {''.join(e.args)}"
                    raise
        
    def CaptureImage(self, sampleId, initials, comments, suffixByTimestamp, captureImageTimeoutSeconds):
        commandWithParameters = f"Capture;{sampleId};{initials};{comments};{suffixByTimestamp}";
        self.SendCommandWithRetry(commandWithParameters, "CaptureOK", captureImageTimeoutSeconds)
                
    def WaitForAnalysisComplete(self, analysisTimeoutSeconds):
        commandWithParameters = f"WaitForAnalysisComplete;{analysisTimeoutSeconds}";
        self.SendCommandWithRetry(commandWithParameters, "AnalysisComplete", analysisTimeoutSeconds)
        
    # Returns when the sphere is up           
    def WaitForSphereUp(self, sphereUpTimeoutSeconds):
        commandWithParameters = f"WaitForSphereUp;{sphereUpTimeoutSeconds}";
        self.SendCommandWithRetry(commandWithParameters, "SphereIsUp", sphereUpTimeoutSeconds)
           
    # If the analysis of the last image failed then an error message with detailes is thrown as an exception
    def CheckIfLastImageFailed(self):
        self.SendCommandWithRetry("LastImageFailed", "False", defaultReadTimeout)
        
        