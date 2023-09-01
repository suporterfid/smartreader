//var Vue = require('vue');

var vueApplication = new Vue({
	el: '#configApp',

	data: {
		jsonImageSummaryData: {},
		showImageSummaryPanelBody: true,
		input: {
			username: "",
			password: ""
		},
		authenticated: true,
		adminAccount: {
			username: "",
			password: ""
		},
		applicationLabel: 'version 4.0.0.26',
		applicationBy: 'Smartreader R700',
		//applicationBy: 'SoLink R700',

		applicationLogo: 'none',
		selectTxPowerOptions: [],
        selectRxSensitivityOptions: [],
		readerLicenses: [],
		readerCapabilitiesList: [],
		readerConfigs: [],
		readerStatuses: [],
		rfidStatus: [],
		txList: [],
		readerSN: '',
		diskspace: '',
		antennaConfigGroup: [],
		readerMaxAntennas: 4,
		timer: '',
		restoredReaderConfig: {},
		file: null,
		content: null,
		tagPopulationIsValid: true,
		sessionIsValid: true

	},


	ready: function () {
		this.fetchReaderCap();
		this.fetchRfidStatus();		
		this.fetchReaderConfig();		
		this.fetchReaderStatus();
		this.fetchReaderSerial();
		this.populateTxList();
		this.parseAntennaConfig();
		this.fetchImageSummaryStatus();
		this.timer = setInterval(this.fetchReaderStatus, 2000);

	},
	
	computed: {
		viewSourceUrl: function () {
			logUrl = 'http://view-source:'+window.location.host+'/api/log';
			console.log('viewSourceUrl: '+logUrl);
			return logUrl;
		},
		isChrome: function () {
			userAgent = window.navigator.userAgent.toLowerCase();
			console.log('userAgent: '+userAgent);
			return userAgent.indexOf("chrome") > -1 || userAgent.indexOf("edge") > -1;
		},

		selectTxPowerOptions: function (){
			var txPowerOptions = [];
			if(this.readerCapabilitiesList != null && this.readerCapabilitiesList[0].hasOwnProperty('txTable'))
			{
				var txList = this.readerCapabilitiesList[0].txTable;
				
				var txListLength = txList.length;
				for (var i = 0; i < txListLength; i++) {
					txPowerOptions.push({
						text: txList[i],
						value: parseInt(txList[i])
				    });
				}
				
			}
			return txPowerOptions;			
		},
		selectRxSensitivityOptions: function (){
			var rxSensitivityOptions = [];
			if(this.readerCapabilitiesList != null && this.readerCapabilitiesList[0].hasOwnProperty('rxTable'))
			{
				var rxList = this.readerCapabilitiesList[0].rxTable;
				
				var rxListLength = rxList.length;
				for (var i = 0; i < rxList.length; i++) {

					rxSensitivityOptions.push({
						text: rxList[i],
						value: parseInt(rxList[i])
					});	

								
				}

			}
			return rxSensitivityOptions;			
		}		
	  },

	methods: {
		downloadFile() {
			let encodedBasicData = btoa(this.adminAccount.username + ':' + this.adminAccount.password);
			this.$http.get('/api/settings', {
				headers: {
					'Authorization': 'Basic ' + encodedBasicData
				}
			})
				.success(function (readerConfigs) {
					console.log("readerConfigs[0] " + JSON.stringify(readerConfigs[0]));
					var fileURL = window.URL.createObjectURL(new Blob([JSON.stringify(readerConfigs[0])], { type: "text/json;charset=utf-8" }));
					
					var fURL = document.createElement('a');

					fURL.href = fileURL;
					fURL.setAttribute('download', 'settings.json');
					document.body.appendChild(fURL);
					fURL.click();
				})
				.error(function (err) {
					console.log(err);
				});
		},
		readConfigFile(event) {

			this.file = event.target.files[0];
			const reader = new FileReader();
			if (this.file.name.includes(".json")) {
				reader.onload = (res) => {
					console.log("res.target.result " + res.target.result);
					this.content = res.target.result;
					this.restoredReaderConfig = JSON.parse(this.content);

					// POST /apply-settings
					this.$http.post('/api/settings', this.restoredReaderConfig).then((response) => {

						// get status
						console.log(response.status);

						// get status text
						console.log(response.statusText);

						// get 'Expires' header
						//console.log(response.headers.get('Expires'));

						// set data on vm
						//this.$set('someData', response.body);
						console.log('Settings applied!!');
						this.fetchReaderConfig();
						this.alertSettingsApplied();
					}, (response) => {
						// error callback
						console.log('Error applying settings:' + err);
						this.alertSettingsNotApplied();
					});
				};
				reader.onerror = (err) => console.log(err);
				reader.readAsText(this.file);

				
			} else {
				this.content = "check the console for file output";
				reader.onload = (res) => {
					console.log(res.target.result);
				};
				reader.onerror = (err) => console.log(err);
				reader.readAsText(this.file);
			}
		},
		onPickFile() {
			this.$refs.configFileInput.click()
		},
		onFilePicked() {
			this.readConfigFile();
		},

		togglePanelBody: function () {
			this.showImageSummaryPanelBody = !this.showImageSummaryPanelBody;
		},

		alertSettingsApplied: function (event) {
		      alert('Settings applied, please stop and then reload the application.');
		    } ,
		
		alertSettingsNotApplied: function (event) {
		      alert('Error applying settings.');
		    } ,
		
	    alertUnisntallRequested: function (event) {
		      alert('Uninstall requested!');
		},

		alertReloadRequested: function (event) {
			alert('Application Reload requested!');
		},


		alertRestoreRequested: function (event) {
			alert('Configuration Restore requested!');
		},
		    
	    alertDataCleanupRequested: function (event) {
		      alert('Data clean-up requested!');
		},

		alertTagPopulation: function (event) {
			
			alert('Settings not applied. Tag Population Estimate must be greater than 0.');
		},

		alertSession: function (event) {

			alert('Settings not applied. TagFocus requires Session 1.');
		},
		
		fetchReaderConfig: function () {
			var readerConfigs = [];

			this.$http.get('/api/settings')
			.success(function (readerConfigs) {
				this.$set('readerConfigs', readerConfigs);
				console.log('readerConfigs: '+readerConfigs);
				console.log('readerConfigs[0].antennaPorts: '+readerConfigs[0].antennaPorts);
				console.log('readerConfigs[0].advancedGpoEnabled: '+readerConfigs[0].advancedGpoEnabled);
				console.log('readerConfigs[0].advancedGpoMode1: '+readerConfigs[0].advancedGpoMode1);
				
			})
			.error(function (err) {
				console.log(err);
			});
		},

		fetchImageSummaryStatus: function () {
			var imageSummaryStatus = {};

			this.$http.get('/api/image')
				.success(function (imageSummaryStatus) {
					this.$set('jsonImageSummaryData', imageSummaryStatus);
					console.log('jsonImageSummaryData: ' + imageSummaryStatus);
				})
				.error(function (err) {
					console.log(err);
				});
		},

		fetchRfidStatus: function () {
			var rfidStatus = [];

			this.$http.get('/api/getrfidstatus')
			.success(function (rfidStatus) {
				this.$set('rfidStatus', rfidStatus);
				console.log('rfidStatus: '+rfidStatus);
				console.log('rfidStatus[0].Antenna1OperationalStatus: '+rfidStatus[0].Antenna1OperationalStatus);			
			})
			.error(function (err) {
				console.log(err);
			});
		},
		
		fetchReaderStatus: function () {
			var readerStatuses = [];
			//let encodedBasicData = btoa(this.adminAccount.username + ':' + this.adminAccount.password);
			this.$http.get('/api/getstatus')
			.success(function (readerStatuses) {
				this.$set('readerStatuses', readerStatuses);
				console.log('readerStatuses: '+readerStatuses);
				console.log('readerStatuses[0].status: '+readerStatuses[0].status);
			})
				.error(function (err) {
					let readerStatus = { status: 'RELOADING' };
					readerStatuses.push(readerStatus);
					this.$set('readerStatuses', readerStatuses);
					console.log('readerStatuses: ' + readerStatuses);
					console.log('readerStatuses[0].status: ' + readerStatuses[0].status);
					console.log(err);
			});
		},
		
		fetchReaderCap: function () {
			var readerCapabilitiesListTemp = [];
			//let encodedBasicData = btoa(this.adminAccount.username + ':' + this.adminAccount.password);
			this.$http.get('/api/getcapabilities')
			.success(function (readerCapabilitiesListTemp) {
				this.$set('readerCapabilitiesList', readerCapabilitiesListTemp);
				console.log('readerCapabilitiesList: '+readerCapabilitiesListTemp);
				console.log('readerCapabilitiesListTemp[0].maxAntennas: '+readerCapabilitiesListTemp[0].maxAntennas);
				console.log('readerCapabilitiesListTemp[0].maxAntennas: '+readerCapabilitiesListTemp[0].maxAntennas);
			})
			.error(function (err) {
				console.log(err);
			});
		},
		
		fetchReaderSerial: function () {
			var readerSerialNumber = [];
			//let encodedBasicData = btoa(this.adminAccount.username + ':' + this.adminAccount.password);
			this.$http.get('/api/getserial')
			.success(function (readerSerialNumber) {
				 var readerSerialToDisplay = readerSerialNumber[0].serialNumber.replace(/-/g , '');
                 this.$set('readerSN', readerSerialToDisplay);
                 console.log('readerSN: '+readerSerialToDisplay);		
				
			})
			.error(function (err) {
				this.$set('readerSN', 'Unknow');
				console.log(err);
			});
		},
		
		fetchDiskSpace: function () {
			var currentDiskspace = [];
			
			this.$http.get('/api/diskspace')
			.success(function (currentDiskspace) {
				 var diskspaceToDisplay = currentDiskspace[0].diskspace;
                 this.$set('diskspace', diskspaceToDisplay);
                 console.log('diskspaceToDisplay: '+diskspaceToDisplay);		
				
			})
			.error(function (err) {
				this.$set('diskspace', 'Unknow');
				console.log(err);
			});
		},
		
		
		populateTxList: function () {
			var readerCapabilitiesList = [];
			//let encodedBasicData = btoa(this.adminAccount.username + ':' + this.adminAccount.password);
			this.$http.get('/api/getcapabilities')
			.success(function (readerCapabilitiesList) {
				this.$set('readerCapabilitiesList', readerCapabilitiesList);
				console.log('populateTxList - readerCapabilitiesList[0].maxAntennas: '+readerCapabilitiesList[0].maxAntennas);
				if(readerCapabilitiesList != null)
				{
					//var txList = this.readerCapabilitiesList[0].txTable.split(",");
					var txList = this.readerCapabilitiesList[0].txTable;
					var txListLength = txList.length;
					for (var i = 0; i < txListLength; i++) {
						console.log('txList['+i+']: '+txList[i]);
					}
					
					this.$set('txList', txList);
				}
			})
			.error(function (err) {
				console.log(err);
			});
			
			
		},

		startApp: function () {
			var appReq = [];
			//let encodedBasicData = btoa(this.adminAccount.username + ':' + this.adminAccount.password);
			this.$http.get('/api/start-inventory')
			.success(function (appReq) {
				this.fetchReaderStatus();
			})
			.error(function (err) {
				console.log(err);
				this.alertSettingsNotApplied();
			});
		},
		
		stopApp: function () {
			var appReq = [];
			//let encodedBasicData = btoa(this.adminAccount.username + ':' + this.adminAccount.password);
			this.$http.get('/api/stop-inventory')
			.success(function (appReq) {
				this.fetchReaderStatus();
			})
			.error(function (err) {
				console.log(err);
				this.alertSettingsNotApplied();
			});
		},

		upgradeFirmware: function () {
			var appReq = [];
			this.$http.get('/api/upgrade-firmware')
				.success(function (appReq) {
					this.fetchReaderStatus();
				})
				.error(function (err) {
					console.log(err);
					this.alertSettingsNotApplied();
				});
		},

		verifyLicense: function () {
			var readerLicenses = [];
			//let encodedBasicData = btoa(this.adminAccount.username + ':' + this.adminAccount.password);
			this.$http.get('/api/verify_key/' + this.readerConfigs[0].licenseKey)
			.success(function (readerLicenses) {
				
				if('pass' == readerLicenses[0].isValid)
				{
					//readerLicenses[0].isValid = 'License is valid.';
					readerLicenses[0].isValid =' <div class="alert alert-success fade in"> '+
				        ' <strong>Success</strong> License is valid.' +
				    '</div> ';
					
				}
				else
				{
					//readerLicenses[0].isValid = 'Unable to verify license.';
					readerLicenses[0].isValid =' <div class="alert alert-danger fade in"> '+
			        ' <strong>Error!</strong> Unable to validate the license.' +
			    '</div> ';
				}
				this.$set('readerLicenses', readerLicenses);
				this.$set('readerConfigs', readerConfigs);
				
				console.log('readerLicenses: '+readerLicenses);
				console.log('readerLicenses[0].isValid: '+readerLicenses[0].isValid);
			})
			.error(function (err) {
				console.log(err);
			});
		},
		
		runRshell: function () {
			this.$http.get('/api/rshell_cmd?command='+this.rshellcmd)
			.success(function (readerLicenses) {
				

			})
			.error(function (err) {
				console.log(err);
			});
		},

		deleteFiles: function () {
			var readerLicenses = [];

			this.$http.get('/api/cleanupdata')
			.success(function () {				
				console.log('deleteFiles: OK.');
				this.alertDataCleanupRequested();			
			})
			.error(function (err) {
				console.log(err);
			});
		},
		
		
		parseAntennaConfig: function () {
			var antennaPortsList = {};
			var enabledAntennasList = {};
			var selectedTxList = {};
			var selectedRxList = {};
			var maxAntennasCount = 4;
			var antennaZonesList = {};

			var readerCapabilitiesList = [];
			var rfidStatus = [];
			var readerConfigs = [];
			//let encodedBasicDataCap = btoa(this.adminAccount.username + ':' + this.adminAccount.password);
			this.$http.get('/api/getcapabilities')
			.success(function (readerCapabilitiesList) {
				this.readerCapabilitiesList = readerCapabilitiesList;
				this.$set('readerCapabilitiesList', readerCapabilitiesList);
				
				console.log('parseAntennaConfig - readerCapabilitiesList[0].maxAntennas: ' + readerCapabilitiesList[0].maxAntennas);
				//let encodedBasicData = btoa(this.adminAccount.username + ':' + this.adminAccount.password);
				this.$http.get('/api/getrfidstatus')
				.success(function (rfidStatus) {
					this.rfidStatus = rfidStatus;
					this.$set('rfidStatus', rfidStatus);
					console.log('parseAntennaConfig - rfidStatus: '+rfidStatus);
					console.log('parseAntennaConfig - rfidStatus[0].Antenna1OperationalStatus: '+rfidStatus[0].Antenna1OperationalStatus);
					
					this.$http.get('/api/settings')
					.success(function (readerConfigs) {
						this.readerConfigs = readerConfigs;
						if(this.readerConfigs[0] != null && this.readerConfigs[0].hasOwnProperty('antennaPorts'))
						{
							antennaPortsList = this.readerConfigs[0].antennaPorts.split(",");
						}

						if(this.readerConfigs[0] != null && this.readerConfigs[0].hasOwnProperty('antennaStates'))
						{
							enabledAntennasList = this.readerConfigs[0].antennaStates.split(",");
						}

						if(this.readerConfigs[0] != null && this.readerConfigs[0].hasOwnProperty('transmitPower'))
						{
							selectedTxList = this.readerConfigs[0].transmitPower.split(",");				
						} 

						if(this.readerConfigs[0] != null && this.readerConfigs[0].hasOwnProperty('receiveSensitivity'))
						{
							selectedRxList = this.readerConfigs[0].receiveSensitivity.split(",");
						}
						
						if(this.readerConfigs[0] != null && this.readerConfigs[0].hasOwnProperty('antennaZones'))
						{
							antennaZonesList = this.readerConfigs[0].antennaZones.split(",");
						}
						
						if(this.readerCapabilitiesList[0] != null && this.readerCapabilitiesList[0].hasOwnProperty('maxAntennas'))
						{
							maxAntennasCount = parseInt(this.readerCapabilitiesList[0].maxAntennas);
							
							if(antennaPortsList.length < maxAntennasCount)
								{
									var antennaPortCounter = antennaPortsList.length;
									while(antennaPortCounter < maxAntennasCount )
										{
											antennaPortCounter = antennaPortCounter + 1;
											antennaPortsList.push(''+antennaPortCounter);
											enabledAntennasList.push('1');
											selectedTxList.push('81');
											selectedRxList.push('0');	
											antennaZonesList.push('ANT');
										}
								}
						}
						this.readerConfigs = readerConfigs;
						this.$set('readerConfigs', readerConfigs);
						console.log('parseAntennaConfig - readerConfigs: '+readerConfigs);
						console.log('parseAntennaConfig - readerConfigs[0].antennaPorts: '+readerConfigs[0].antennaPorts);	
						
						var antennaConfigArray = new Array();

						//for (var i = 0; i < antennaPortsList.length; i++) {
						for (var i = 0; i < maxAntennasCount; i++) {
							var antennaPortNumberForDebug = i+1;
						    var antennaPortTemp = antennaPortsList[i];
						    var enabledAntennaTemp = enabledAntennasList[i];
						    var selectedTxTemp = selectedTxList[i];
						    var selectedRxTemp = selectedRxList[i];
						    var antennaZoneTemp = antennaZonesList[i];

						    console.log('parseAntennaConfig - selectedTxPowerAntennaPort'+antennaPortNumberForDebug+': '+selectedTxTemp);
							if(selectedTxList[i] == '90' && this.txList[this.txList.length - 1] == 3000)
							{
								selectedTxTemp = '81';
							}  
							
							var operationalStatusPropertyToCheck = 'Antenna'+antennaPortNumberForDebug+'OperationalStatus';
							var currentRfidStatus = rfidStatus[0];
							var currentAntennaOperationalStatusValue = currentRfidStatus[operationalStatusPropertyToCheck];

							var lastPowerLevelPropertyToCheck = 'Antenna'+antennaPortNumberForDebug+'LastPowerLevel';
							var antennaLastPowerLevel = currentRfidStatus[lastPowerLevelPropertyToCheck];
							
							antennaConfigArray.push({
						        "antennaPortNumber" : antennaPortTemp,
						        "isAntennaEnabled"  : enabledAntennaTemp,
						        "antennaSelectedTx"	: selectedTxTemp,
						        "antennaSelectedRx"	: selectedRxTemp,
						        "antennaOperationalStatus" : currentAntennaOperationalStatusValue,
						        "antennaLastPowerLevel"	: antennaLastPowerLevel,	
						        "antennaZone"	: antennaZoneTemp	
							});
							//console.log('parseAntennaConfig - selectedTxPowerAntennaPort' + antennaPortNumberForDebug + ': ' + selectedTxTemp)
						}

						var antennaConfigJson = JSON.stringify(antennaConfigArray);

						console.log('parseAntennaConfig - antennaConfigJson: '+antennaConfigJson);

						this.$set('antennaConfigGroup', antennaConfigArray);
						this.antennaConfigGroup = antennaConfigArray;
						this.$set('readerMaxAntennas', maxAntennasCount);
						this.readerMaxAntennas = maxAntennasCount;
					})
					.error(function (err) {
						console.log(err);
					});
				})
				.error(function (err) {
					console.log(err);
				});
			})
			.error(function (err) {
				console.log(err);
			});		 
		},

		testBearer: function (event) {
			var bearerData = {};
			bearerData.httpAuthenticationTokenApiUrl = this.readerConfigs[0].httpAuthenticationTokenApiUrl;
			bearerData.httpAuthenticationTokenApiBody = this.readerConfigs[0].httpAuthenticationTokenApiBody;
			bearerData.httpAuthenticationTokenApiValue = this.readerConfigs[0].httpAuthenticationTokenApiValue;
			this.$http.post('/api/test', bearerData).then((response) => {

				// get status
				console.log(response.status);

				// get status text
				console.log(response.statusText);

				// get 'Expires' header
				//console.log(response.headers.get('Expires'));
				
				console.log(response);
				alert('Response status [' + response.status + ']. Message ' + response.statusText + '. \n Content: ' + response.data);
				this.readerConfigs[0].httpAuthenticationTokenApiValue = response.data;
				// set data on vm
				//this.$set('someData', response.body);
			}, (response) => {
				// error callback
				console.log('Error applying settings:' + err);
				alert('Error [' + response.status + '] \n ' + response.data);

			});
        },


		applySettings: function (event) {

			console.log('applySettings>>');
			
			console.log('antennaConfigGroup: ' + this.antennaConfigGroup);
			if (this.readerConfigs[0].searchMode === 3 && this.readerConfigs[0].session !== 1) {
				this.sessionIsValid = false;
				this.alertSession();
				return;
			}
			else {
				this.sessionIsValid = true;
			}

				
			if (this.readerConfigs[0].tagPopulation < 1) {
				this.tagPopulationIsValid = false;
				this.alertTagPopulation();
				return;
			}
			else {
				this.tagPopulationIsValid = true;
            }

			this.readerConfigs[0].antennaPorts = '';
			this.readerConfigs[0].antennaStates = '';
			this.readerConfigs[0].transmitPower = '';
			this.readerConfigs[0].receiveSensitivity = '';
			this.readerConfigs[0].antennaZones = '';
			
			for(var i = 0; i < this.antennaConfigGroup.length; ++i) {
				this.readerConfigs[0].antennaPorts += this.antennaConfigGroup[i].antennaPortNumber;
				this.readerConfigs[0].antennaStates += this.antennaConfigGroup[i].isAntennaEnabled;
				this.readerConfigs[0].transmitPower += this.antennaConfigGroup[i].antennaSelectedTx;
				this.readerConfigs[0].receiveSensitivity += this.antennaConfigGroup[i].antennaSelectedRx;
				this.readerConfigs[0].antennaZones += this.antennaConfigGroup[i].antennaZone;				
			    if(i < this.antennaConfigGroup.length - 1){
			    	this.readerConfigs[0].antennaPorts += ',';
					this.readerConfigs[0].antennaStates += ',';
					this.readerConfigs[0].transmitPower +=  ',';
					this.readerConfigs[0].receiveSensitivity += ',';
					this.readerConfigs[0].antennaZones += ',';
			    }
			}
			
			//sanitize URLs:
			for (let keyName in this.readerConfigs[0]) {
			    let value = encodeURIComponent(this.readerConfigs[0][keyName]);
			    this.readerConfigs[0][keyName] = value;
			    console.log('this.readerConfigs[0]['+keyName+']:'+this.readerConfigs[0][keyName]);
			}
			
			if(this.readerConfigs[0].toiValidationEnabled === 1) {
				this.readerConfigs[0].advancedGpoEnabled = 1;
				this.readerConfigs[0].advancedGpoMode1 = 0;
				this.readerConfigs[0].advancedGpoMode2 = 0;
				this.readerConfigs[0].advancedGpoMode3 = 0;
				this.readerConfigs[0].advancedGpoMode4 = 0;
				this.readerConfigs[0].includeGpiEvent = 1;
			}
			
			console.log('Applying settings:');
			console.log('readerConfigs[0].antennaPorts:'+this.readerConfigs[0].antennaPorts);
			console.log('readerConfigs[0].antennaStates:'+this.readerConfigs[0].antennaStates);
			console.log('readerConfigs[0].transmitPower:'+this.readerConfigs[0].transmitPower);
			console.log('readerConfigs[0].receiveSensitivity:'+this.readerConfigs[0].receiveSensitivity);
			console.log('readerConfigs[0].antennaZones:'+this.readerConfigs[0].antennaZones);
			
			// POST /apply-settings
			this.$http.post('/api/settings', this.readerConfigs[0]).then((response) => {

		    // get status
			 console.log(response.status);

		    // get status text
		    console.log(response.statusText);

		    // get 'Expires' header
		    //console.log(response.headers.get('Expires'));

		    // set data on vm
		    //this.$set('someData', response.body);
		    console.log('Settings applied!!');
		    this.fetchReaderConfig();
		    this.alertSettingsApplied();
		  }, (response) => {
		    // error callback
			  console.log('Error applying settings:'+err);
			  this.alertSettingsNotApplied();
		  });
			  


		},

		uninstall: function (event) {
			this.$http.get('/api/removecap',this.event)
			.success(function (res) {
				console.log('Uninstalling');
				this.alertUnisntallRequested();
			})
			.error(function (err) {
				console.log(err);
			});
		},

		reloadApp: function (event) {
			this.$http.get('/api/reload', this.event)
				.success(function (res) {
					console.log('Reloading');
					this.alertReloadRequested();
				})
				.error(function (err) {
					console.log(err);
				});
		},

		restoreConfig: function (event) {
			this.$http.get('/api/restore', this.event)
				.success(function (res) {
					console.log('Restring');
					this.alertRestoreRequested();
				})
				.error(function (err) {
					console.log(err);
				});
		},
		uploadCaCert(event) {
			let data = new FormData();
			let file = event.target.files[0];

			//data.append('name', 'my-file')
			data.append('files', file)

			let headerConfig = {
				header: {
					'Content-Type': 'multipart/form-data'
				}
			}
			// POST /apply-settings
			this.$http.post('/upload/mqtt/ca', data , headerConfig).then((response) => {

				// get status
				console.log(response.status);

				// get status text
				console.log(response.statusText);

				// get 'Expires' header
				//console.log(response.headers.get('Expires'));

				// set data on vm
				//this.$set('someData', response.body);
				console.log('Settings applied!!');
				this.fetchReaderConfig();
				this.alertSettingsApplied();
			}, (response) => {
				// error callback
				console.log('Error applying settings:' + err);
				this.alertSettingsNotApplied();
			});
		},
		uploadClientCert(event) {
			let data = new FormData();
			let file = event.target.files[0];

			//data.append('name', 'my-file')
			data.append('files', file)

			let headerConfig = {
				header: {
					'Content-Type': 'multipart/form-data'
				}
			}
			// POST /apply-settings
			this.$http.post('/upload/mqtt/certificate', data, headerConfig).then((response) => {

				// get status
				console.log(response.status);

				// get status text
				console.log(response.statusText);

				// get 'Expires' header
				//console.log(response.headers.get('Expires'));

				// set data on vm
				//this.$set('someData', response.body);
				console.log('Settings applied!!');
				this.fetchReaderConfig();
				this.alertSettingsApplied();
			}, (response) => {
				// error callback
				console.log('Error applying settings:' + err);
				this.alertSettingsNotApplied();
			});
		},
		cancelAutoUpdate () {
            clearInterval(this.timer);
		}

	},

	beforeDestroy () {
		this.cancelAutoUpdate();
	  }
});
