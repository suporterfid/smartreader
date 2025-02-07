https://support.impinj.com/hc/en-us/articles/360000468370-Software-Tools-License-Disclaimer

PLEASE READ THE FOLLOWING LICENSE & DISCLAIMER ("AGREEMENT") CAREFULLY BEFORE USING ANY SOFTWARE TOOLS (AS DEFINED BELOW) MADE AVAILABLE TO YOU ("LICENSEE") BY IMPINJ, INC. ("IMPINJ"). BY USING THE SOFTWARE TOOLS, YOU ACKNOWLEDGE THAT YOU HAVE READ AND UNDERSTOOD ALL THE TERMS AND CONDITIONS OF THE AGREEMENT, YOU WILL BE CONSENTING TO BE BOUND BY THEM, AND YOU ARE AUTHORIZED TO DO SO. IF YOU DO NOT ACCEPT THESE TERMS AND CONDITIONS, DO NOT USE THE SOFTWARE TOOLS.

=======

# SmartReader R700

## Overview

SmartReader R700 is a sophisticated Custom Application (CAP) reference design specifically developed for Impinj R700 RAIN RFID Readers. It provides an integrated solution for configuring and managing RFID operations while offering flexible data publishing options through the Impinj IoT Interface.

The application combines intuitive user interface design with robust backend processing, making it ideal for both simple deployments and complex industrial environments. While not intended as a complete inventory management solution, it serves as a powerful tool for RFID data collection and distribution.

## Key Features

### Data Output Capabilities

SmartReader R700 supports multiple data output channels, allowing flexible integration with existing systems:

- Local HTTP streams directly on the reader
- MQTT broker communication (supporting both TCP and WebSocket protocols)
- HTTP POST endpoints
- TCP/IP socket connections
- UDP socket communication
- Serial port over USB interface
- Direct writing to USB Flash Drive

### RFID Operations

The application provides comprehensive RFID tag reading capabilities:

- EPC (Electronic Product Code) reading
- TID (Tag Identifier) collection
- User Memory data access
- Timestamp recording
- Antenna port number tracking
- Antenna zone name management
- GTIN/SGTIN Decoding support

### Hardware Integration

- Barcode scanner integration (USB and Network variants)
- Smart shelf/cabinet support
- Batch list reporting for monitoring tag additions/removals
- Antenna zone group management

## Hardware Compatibility

### Supported Reader Models
- IPJ-R700-241 (including Antenna Hub functionality)
- IPJ-R700-341 (including Antenna Hub functionality)

### Firmware Requirements
- Compatible with firmware version 8.4.0.240 and newer
- Regular firmware updates recommended for optimal performance

## Configuration Interface

The graphical user interface provides intuitive access to:

1. Reader Settings
   - Basic configuration parameters
   - Direct-to-memory configuration saving
   - Antenna zone grouping
   - Power level adjustment
   - Frequency settings

2. Data Collection
   - Tag memory bank selection
   - Reading mode configuration
   - Filtering options
   - Report formatting

3. Output Management
   - Protocol selection
   - Connection parameters
   - Data format configuration
   - Buffer settings

### Enhanced Stream Processing

SmartReader implements advanced timeout detection and recovery mechanisms for HTTP stream processing, ensuring reliable operation even in challenging network conditions. The system maintains data integrity while gracefully handling various error scenarios that commonly occur in industrial IoT deployments.

Key capabilities include:

1. **Timeout Management**
   - Configurable timeout thresholds with a default of 10 seconds
   - CancellationTokenSource integration for precise operation control
   - Automatic retry mechanism with exponential backoff
   - Thread-safe operation handling

2. **Error Recovery**
   - Specialized recovery paths for different error types
   - Automatic stream state validation
   - Comprehensive error logging with context preservation
   - Non-blocking recovery operations

3. **Stream Processing**
   - Real-time data validation
   - Structured logging with correlation IDs
   - Configurable processing parameters
   - Memory-efficient operation

### Dynamic Logging Management

The application provides sophisticated runtime log level control through a secure REST API, enabling authorized users to adjust logging verbosity without service interruption. This feature is particularly valuable for production troubleshooting and performance optimization.

Key features include:

1. **REST API Integration**
   - Endpoint: `/api/logging/level`
   - Secure access through basic authentication
   - Support for DEBUG, INFORMATION, WARNING, and ERROR levels
   - Real-time level switching using Serilog

2. **Windows Automation**
   - Command-line management script: `set-log-level.bat`
   - Case-insensitive input handling
   - Clear operation feedback
   - Exit code support for automation

3. **Security**
   - Integration with existing authentication layer
   - Audit logging for level changes
   - Thread-safe operations
   - Role-based access control ready
   
## Smart Features

### Batch List Reporting
The system implements intelligent tag state tracking:

1. Zone Monitoring
   - Real-time tag presence detection
   - Addition/subtraction tracking
   - Zone-based filtering
   - Customizable reporting intervals

2. Smart Shelf Integration
   - Shelf occupancy monitoring
   - Product movement tracking
   - Alert generation capabilities
   - Zone-based inventory management

### Advanced Data Processing

1. GTIN/SGTIN Decoding
   - Automatic format detection
   - Standards compliance checking
   - Error correction
   - Filtered reporting

2. Multi-Source Integration
   - Barcode scanner data fusion
   - RFID tag correlation
   - Timestamp synchronization
   - Unified data output

## Building the Project

The project uses Docker for consistent builds across different environments. The build process generates an .upgx file compatible with R700 readers.

### Windows Build

```cmd
cd SmartReaderStandalone
build-upgx-docker.bat
```

### Linux Build

```bash
cd SmartReaderStandalone
chmod +x build-upgx-docker.sh
./build-upgx-docker.sh
```

### Build Process Details

The build scripts perform these operations:

1. Clean up Docker system
2. Build the Docker image using Dockerfile
3. Run a container from the image
4. Copy the generated .upgx file to cap_deploy/
5. Remove the temporary container

## System Requirements

### Development Environment
- Docker Engine 20.10 or newer
- docker-compose 1.29 or newer
- 4GB RAM minimum
- 10GB free disk space

### Runtime Requirements
- .NET 6.0 Runtime or newer
- Windows Server 2019/2022 or Linux with systemd
- Network access to RFID readers
- HTTPS certificate for secure communication

## Configuration

### Stream Processing Settings

Configure timeout and retry behavior in appsettings.json:

```json
{
  "StreamProcessing": {
    "DefaultTimeoutSeconds": 10,
    "MaxRetryAttempts": 3,
    "RetryDelayMilliseconds": 1000,
    "MaxConcurrentStreams": 5
  }
}
```

### Logging Configuration

Manage logging through the REST API:

```bash
# Set log level to DEBUG
curl -X POST https://your-server/api/logging/level \
  -H "Authorization: Basic your-credentials" \
  -H "Content-Type: application/json" \
  -d '{"level": "DEBUG"}'

# Get current log level
curl -X GET https://your-server/api/logging/level \
  -H "Authorization: Basic your-credentials"
```

Or use the Windows command-line tool:

```cmd
set-log-level.bat DEBUG
```

## Deployment

1. Build the application using the provided scripts
2. Locate the .upgx file in cap_deploy/ directory
3. Deploy using standard R700 reader upgrade procedures
4. Verify deployment through reader management interface

## Security Considerations

1. **Authentication**
   - All management endpoints require authentication
   - Use HTTPS for API communication
   - Regular credential rotation recommended

2. **Logging**
   - Sensitive data is automatically redacted
   - Log level changes are audited
   - File system permissions are enforced

3. **Network**
   - Firewall rules should restrict access to management API
   - Reader communication uses secure protocols
   - Rate limiting prevents DoS attacks

## Troubleshooting

Common issues and solutions:

1. **Stream Timeouts**
   - Verify network connectivity
   - Check reader firmware version
   - Review timeout settings
   - Examine error logs

2. **Authentication Failures**
   - Verify credentials
   - Check certificate validity
   - Confirm proper authorization headers

3. **Build Issues**
   - Ensure Docker is running
   - Verify disk space
   - Check network access to repositories

## Future Roadmap

Planned enhancements include:

1. **Stream Processing**
   - Enhanced performance metrics
   - Advanced retry strategies
   - Custom timeout policies
   - Improved error recovery

2. **Logging**
   - Persistent configuration
   - Enhanced role-based access
   - Advanced monitoring integration
   - Custom log formatting

3. **Security**
   - OAuth2 support
   - Enhanced audit logging
   - Certificate management
   - Security scanning integration

# License 

1. PURPOSE OF AGREEMENT. From time to time, Impinj technical personnel may make available to Licensee certain software, including code (in source and object form), tools, libraries, configuration files, translations, and related documentation (collectively, “Software Tools”), upon specific request or to assist with a specific deployment. This Agreement sets forth Licensee's limited rights and Impinj's limited obligations with respect to the Software Tools. Licensee acknowledges that Impinj provides the Software Tools free of charge. This Agreement does not grant any rights with respect to Impinj standalone software products (e.g., ItemSense, ItemEncode, SpeedwayConnect) or the firmware on Impinj hardware, all of which are subject to separate license terms.

2. LIMITED LICENSE. Subject to the terms and conditions of this Agreement, Impinj hereby grants to Licensee a limited, royalty-free, worldwide, non-exclusive, perpetual and irrevocable (except as set forth below), non-transferable license, without right of sublicense, to (a) use the Software Tools and (b) only with respect to Software Tools provided in source code form, modify and create derivative works of such Software Tools, in each case, solely for Licensee’s internal development related to the deployment of Impinj products (“Purpose”). The Software Tools may only be used by employees of Licensee that must have access to the Software Tools in connection with the Purpose.

3. TERMINATION. Impinj may immediately terminate this Agreement if Licensee breaches any provision hereof. Upon the termination of this Agreement, Licensee must (a) discontinue all use of the Software Tools, (b) uninstall the Software Tools from its systems, (c) destroy or return to Impinj all copies of the Software Tools and any other materials provided by Impinj, and (d) promptly provide Impinj with written confirmation (including via email) of Licensee’s compliance with these provisions. Sections 4-10 will survive termination of this Agreement.

4. OWNERSHIP. The Software Tools are licensed, not sold, by Impinj to Licensee. Impinj and its suppliers own and retain all right, title, and interest, including all intellectual property rights, in and to the Software Tools. Except for those rights expressly granted in this Agreement, no other rights are granted, either express or implied, to Licensee. Impinj reserves the right to develop, price and sell software products that have features similar to or competitive with Software Tools. Licensee grants Impinj a limited, royalty-free, worldwide, perpetual and irrevocable, transferable, sublicensable, license to Licensee’s derivative works of Software Tools; provided that Licensee has no obligation under this Agreement to deliver to Impinj any such derivative works.

5. CONFIDENTIALITY. In order to protect the trade secrets and proprietary know-how contained in the Software Tools, Licensee will not decompile, disassemble, or reverse engineer, or otherwise attempt to gain access to the source code or algorithms of the Software Tools (unless Impinj provides the Software Tools in source code format). Licensee will maintain the confidentiality of and not disclose to any third party: (a) all non-public information disclosed by Impinj to Licensee under this Agreement and (b) all performance data and all other information obtained through the Software Tools.

6. WARRANTY DISCLAIMER. LICENSEE ACKNOWLEDGES THAT IMPINJ PROVIDES THE SOFTWARE TOOLS FREE OF CHARGE AND ONLY FOR THE PURPOSE. ACCORDINGLY, THE SOFTWARE TOOLS ARE PROVIDED “AS IS” WITHOUT QUALITY CHECK, AND IMPINJ DOES NOT WARRANT THAT THE SOFTWARE TOOLS WILL OPERATE WITHOUT ERROR OR INTERRUPTION OR MEET ANY PERFORMANCE STANDARD OR OTHER EXPECTATION. IMPINJ EXPRESSLY DISCLAIMS ALL WARRANTIES, EXPRESS OR IMPLIED, INCLUDING THE IMPLIED WARRANTIES OF MERCHANTABILITY, NONINFRINGEMENT, QUALITY, ACCURACY, AND FITNESS FOR A PARTICULAR PURPOSE. IMPINJ IS NOT OBLIGATED IN ANY WAY TO PROVIDE SUPPORT OR OTHER MAINTENANCE WITH RESPECT TO THE SOFTWARE TOOLS.

7. LIMITATION OF LIABILITY. THE TOTAL LIABILITY OF IMPINJ ARISING OUT OF OR RELATED TO THE SOFTWARE TOOLS WILL NOT EXCEED THE TOTAL AMOUNT PAID BY LICENSEE TO IMPINJ PURSUANT TO THIS AGREEMENT. IN NO EVENT WILL IMPINJ HAVE LIABILITY FOR ANY INDIRECT, INCIDENTAL, SPECIAL, OR CONSEQUENTIAL DAMAGES, EVEN IF ADVISED OF THE POSSIBILITY OF THESE DAMAGES. THESE LIMITATIONS WILL APPLY NOTWITHSTANDING ANY FAILURE OF ESSENTIAL PURPOSE OF ANY LIMITED REMEDY IN THIS AGREEMENT.

8. THIRD PARTY SOFTWARE. The Software Tools may contain software created by a third party. Licensee’s use of any such third party software is subject to the applicable license terms and this Agreement does not alter those license terms. Licensee may not subject any portion of the Software Tools to an open source license.

9. RESTRICTED USE. Licensee will comply with all applicable laws and regulations to preclude the acquisition by any governmental agency of unlimited rights to technical data, software, and documentation provided with Software Tools, and include the appropriate “Restricted Rights” or “Limited Rights” notices required by the applicable U.S. or foreign government agencies. Licensee will comply in all respects with all U.S. and foreign export and re-export laws and regulations applicable to the technology and documentation provided hereunder.

10. MISCELLANEOUS. This Agreement will be governed by the laws of the State of Washington, U.S.A without reference to conflict of law principles. All disputes arising out of or related to it, will be subject to the exclusive jurisdiction of the state and federal courts located in King County, Washington, and the parties agree and submit to the personal and exclusive jurisdiction and venue of these courts. Licensee will not assign this Agreement, directly or indirectly, by operation of law or otherwise, without the prior written consent of Impinj. This Agreement (and any applicable nondisclosure agreement) is the entire agreement between the parties relating to the Software Tools. No waiver or modification of this Agreement will be valid unless contained in a writing signed by each party.