# smartreader

https://support.impinj.com/hc/en-us/articles/360000468370-Software-Tools-License-Disclaimer

PLEASE READ THE FOLLOWING LICENSE & DISCLAIMER (“AGREEMENT”) CAREFULLY BEFORE USING ANY SOFTWARE TOOLS (AS DEFINED BELOW) MADE AVAILABLE TO YOU (“LICENSEE”) BY IMPINJ, INC. (“IMPINJ”). BY USING THE SOFTWARE TOOLS, YOU ACKNOWLEDGE THAT YOU HAVE READ AND UNDERSTOOD ALL THE TERMS AND CONDITIONS OF THE AGREEMENT, YOU WILL BE CONSENTING TO BE BOUND BY THEM, AND YOU ARE AUTHORIZED TO DO SO. IF YOU DO NOT ACCEPT THESE TERMS AND CONDITIONS, DO NOT USE THE SOFTWARE TOOLS.
 =======
 # SmartReader

 ## Building the Project

 To build the project and generate the .upgx file for the R700 reader:

 ### Windows
 Run the build script:
 ```cmd
 cd SmartReaderStandalone
 build-upgx-docker.bat

  =======
 # SmartReader

 ## Building the Project

 To build the project and generate the .upgx file for the R700 reader:

 ###  Linux
 Run the build script:
 ```bash
 cd SmartReaderStandalone
 chmod +x build-upgx-docker.sh
 ./build-upgx-docker.sh

 ## The scripts will:

 1 Clean up Docker system
 2 Build the Docker image using Dockerfile
 3 Run a container from the image
 4 Copy the generated .upgx file to cap_deploy/ directory
 5 Remove the temporary container

 ## Requirements:

 • Docker must be installed and running
 • docker-compose.yml and Dockerfile must be present in the project directory

The generated .upgx file will be available in the cap_deploy/ directory after successful build.

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