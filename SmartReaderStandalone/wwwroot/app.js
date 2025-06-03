function toStringFields(data, fields) {
	fields.forEach(field => {
		if (field in data && data[field] != null) {
			data[field] = String(data[field]);
		}
	});
}

//var Vue = require('vue');

/**
 * GPO Configuration Manager
 * Handles all GPO-related operations for the SmartReader application
 */
class GpoConfigurationManager {
	constructor() {
		this.apiBaseUrl = '/api/gpo';
		this.maxGpoPorts = 3;
		this.currentConfig = null;
		this.isLoading = false;

		// Bind methods to maintain context
		this.loadConfiguration = this.loadConfiguration.bind(this);
		this.saveConfiguration = this.saveConfiguration.bind(this);
		this.resetToDefaults = this.resetToDefaults.bind(this);
		this.togglePulseDuration = this.togglePulseDuration.bind(this);

		// Initialize when DOM is ready
		this.init();
	}

	/**
	 * Initialize the GPO configuration manager
	 */
	async init() {
		try {
			this.setupEventListeners();
			await this.loadConfiguration();
			this.setupFormValidation();
		} catch (error) {
			console.error('Failed to initialize GPO Configuration Manager:', error);
			this.showError('Failed to initialize GPO configuration');
		}
	}

	/**
	 * Setup event listeners for form interactions
	 */
	setupEventListeners() {
		// Advanced GPO enabled checkbox
		const advancedCheckbox = document.getElementById('advancedGpoEnabled');
		if (advancedCheckbox) {
			advancedCheckbox.addEventListener('change', (e) => {
				this.toggleAdvancedGpoSection(e.target.checked);
			});
		}

		// Control mode change handlers
		for (let i = 1; i <= this.maxGpoPorts; i++) {
			const controlSelect = document.getElementById(`gpo${i}Control`);
			if (controlSelect) {
				controlSelect.addEventListener('change', () => {
					this.togglePulseDuration(i);
					this.validateGpoConfiguration(i);
				});
			}

			// State change handlers
			const stateSelect = document.getElementById(`gpo${i}State`);
			if (stateSelect) {
				stateSelect.addEventListener('change', () => {
					this.validateGpoConfiguration(i);
				});
			}

			// Pulse duration change handlers
			const pulseDurationInput = document.getElementById(`gpo${i}PulseDuration`);
			if (pulseDurationInput) {
				pulseDurationInput.addEventListener('input', () => {
					this.validatePulseDuration(i);
				});
			}
		}
	}

	/**
	 * Setup form validation
	 */
	setupFormValidation() {
		const form = document.querySelector('form');
		if (form) {
			form.addEventListener('submit', (e) => {
				e.preventDefault();
				if (this.validateAllConfigurations()) {
					this.saveConfiguration();
				}
			});
		}
	}

	/**
	 * Toggle the advanced GPO configuration section
	 */
	toggleAdvancedGpoSection(enabled) {
		const configSection = document.getElementById('gpoConfiguration');
		if (configSection) {
			configSection.style.display = enabled ? 'block' : 'none';
		}
	}

	/**
	 * Toggle pulse duration input based on control mode
	 */
	togglePulseDuration(gpoNumber) {
		const controlSelect = document.getElementById(`gpo${gpoNumber}Control`);
		const pulseDurationDiv = document.getElementById(`pulseDuration${gpoNumber}`);

		if (!controlSelect || !pulseDurationDiv) {
			console.warn(`Elements not found for GPO ${gpoNumber}`);
			return;
		}

		if (controlSelect.value === 'pulsed') {
			pulseDurationDiv.classList.add('show');
		} else {
			pulseDurationDiv.classList.remove('show');
		}
	}

	/**
	 * Load GPO configuration from the server
	 */
	async loadConfiguration() {
		this.setLoading(true);

		try {
			const response = await this.makeApiCall('GET', '/configuration');

			if (response.ok) {
				const config = await response.json();
				this.currentConfig = config;
				this.populateForm(config);
				this.showSuccess('Configuration loaded successfully');
			} else {
				throw new Error(`HTTP ${response.status}: ${response.statusText}`);
			}
		} catch (error) {
			console.error('Error loading GPO configuration:', error);
			this.showError('Failed to load configuration: ' + error.message);
			// Load defaults if server call fails
			this.loadDefaultConfiguration();
		} finally {
			this.setLoading(false);
		}
	}

	/**
	 * Load default configuration when server is unavailable
	 */
	loadDefaultConfiguration() {
		const defaultConfig = {
			gpoConfigurations: [
				{ gpo: 1, control: 'Static', state: 'Low' },
				{ gpo: 2, control: 'Static', state: 'Low' },
				{ gpo: 3, control: 'Static', state: 'Low' }
			]
		};

		this.currentConfig = defaultConfig;
		this.populateForm(defaultConfig);
	}

	/**
	 * Populate the form with configuration data
	 */
	populateForm(config) {
		try {
			// Set advanced GPO enabled state
			const advancedEnabled = config.gpoConfigurations && config.gpoConfigurations.length > 0;
			const advancedCheckbox = document.getElementById('advancedGpoEnabled');
			if (advancedCheckbox) {
				advancedCheckbox.checked = advancedEnabled;
				this.toggleAdvancedGpoSection(advancedEnabled);
			}

			// Clear existing form data
			this.clearForm();

			if (config.gpoConfigurations && config.gpoConfigurations.length > 0) {
				// Populate configurations for each GPO (limit to 3)
				const maxConfigs = Math.min(config.gpoConfigurations.length, this.maxGpoPorts);

				for (let i = 0; i < maxConfigs; i++) {
					const gpoConfig = config.gpoConfigurations[i];
					if (gpoConfig.gpo >= 1 && gpoConfig.gpo <= this.maxGpoPorts) {
						this.populateGpoConfiguration(gpoConfig);
					}
				}
			} else {
				// Set defaults for all 3 GPOs
				this.setDefaultGpoValues();
			}

			// Validate all configurations after population
			this.validateAllConfigurations();

		} catch (error) {
			console.error('Error populating form:', error);
			this.showError('Error displaying configuration data');
			this.setDefaultGpoValues();
		}
	}

	/**
	 * Populate configuration for a specific GPO
	 */
	populateGpoConfiguration(gpoConfig) {
		const gpoNumber = gpoConfig.gpo;

		if (gpoNumber < 1 || gpoNumber > this.maxGpoPorts) {
			console.warn(`Invalid GPO number: ${gpoNumber}`);
			return;
		}

		// Set control mode
		const controlSelect = document.getElementById(`gpo${gpoNumber}Control`);
		if (controlSelect && gpoConfig.control) {
			controlSelect.value = this.mapControlModeFromApi(gpoConfig.control);
		}

		// Set state with proper fallback
		const stateSelect = document.getElementById(`gpo${gpoNumber}State`);
		if (stateSelect) {
			if (gpoConfig.state !== undefined && gpoConfig.state !== null) {
				stateSelect.value = this.mapStateFromApi(gpoConfig.state);
			} else {
				// Default to low if state is not specified
				stateSelect.value = 'low';
			}
		}

		// Set pulse duration if applicable
		const pulseDurationInput = document.getElementById(`gpo${gpoNumber}PulseDuration`);
		if (pulseDurationInput && gpoConfig.pulseDurationMilliseconds) {
			pulseDurationInput.value = gpoConfig.pulseDurationMilliseconds;
		}

		// Show/hide pulse duration based on control mode
		this.togglePulseDuration(gpoNumber);
	}

	/**
	 * Clear all form data
	 */
	clearForm() {
		for (let i = 1; i <= this.maxGpoPorts; i++) {
			const controlSelect = document.getElementById(`gpo${i}Control`);
			const stateSelect = document.getElementById(`gpo${i}State`);
			const pulseDurationInput = document.getElementById(`gpo${i}PulseDuration`);

			if (controlSelect) controlSelect.value = 'static';
			if (stateSelect) stateSelect.value = '';
			if (pulseDurationInput) pulseDurationInput.value = '1000';

			this.togglePulseDuration(i);
			this.clearValidationErrors(i);
		}
	}

	/**
	 * Set default values for all GPOs
	 */
	setDefaultGpoValues() {
		for (let i = 1; i <= this.maxGpoPorts; i++) {
			const controlSelect = document.getElementById(`gpo${i}Control`);
			const stateSelect = document.getElementById(`gpo${i}State`);
			const pulseDurationInput = document.getElementById(`gpo${i}PulseDuration`);

			if (controlSelect) controlSelect.value = 'static';
			if (stateSelect) stateSelect.value = 'low';
			if (pulseDurationInput) pulseDurationInput.value = '1000';

			this.togglePulseDuration(i);
		}
	}

	/**
	 * Save GPO configuration to the server
	 */
	async saveConfiguration() {
		if (this.isLoading) {
			console.warn('Save operation already in progress');
			return;
		}

		this.setLoading(true);

		try {
			if (!this.validateAllConfigurations()) {
				throw new Error('Configuration validation failed');
			}

			const config = this.buildConfigurationObject();

			const response = await this.makeApiCall('POST', '/configuration', config);

			if (response.ok) {
				this.currentConfig = config;
				this.showSuccess('GPO configuration saved successfully');
			} else {
				const errorText = await response.text();
				throw new Error(`HTTP ${response.status}: ${errorText}`);
			}
		} catch (error) {
			console.error('Error saving GPO configuration:', error);
			this.showError('Failed to save configuration: ' + error.message);
		} finally {
			this.setLoading(false);
		}
	}

	/**
	 * Build configuration object from form data
	 */
	buildConfigurationObject() {
		const advancedEnabled = document.getElementById('advancedGpoEnabled')?.checked;

		if (!advancedEnabled) {
			return { gpoConfigurations: [] };
		}

		const gpoConfigurations = [];

		for (let i = 1; i <= this.maxGpoPorts; i++) {
			const control = document.getElementById(`gpo${i}Control`)?.value;
			const state = document.getElementById(`gpo${i}State`)?.value;
			const pulseDuration = parseInt(document.getElementById(`gpo${i}PulseDuration`)?.value);

			if (!control) {
				console.warn(`No control mode specified for GPO ${i}`);
				continue;
			}

			const gpoConfig = {
				gpo: i,
				control: this.mapControlModeToApi(control)
			};

			// Only include state for modes that require it
			if ((control === 'static' || control === 'pulsed') && state) {
				gpoConfig.state = this.mapStateToApi(state);
			}

			// Include pulse duration for pulsed mode
			if (control === 'pulsed' && pulseDuration > 0) {
				gpoConfig.pulseDurationMilliseconds = pulseDuration;
			}

			gpoConfigurations.push(gpoConfig);
		}

		return { gpoConfigurations };
	}

	/**
	 * Reset all GPOs to default configuration
	 */
	async resetToDefaults() {
		if (!confirm('Are you sure you want to reset all GPO settings to defaults?')) {
			return;
		}

		this.setLoading(true);

		try {
			const response = await this.makeApiCall('POST', '/reset');

			if (response.ok) {
				// Reload configuration from server
				await this.loadConfiguration();
				this.showSuccess('Configuration reset to defaults');
			} else {
				throw new Error(`HTTP ${response.status}: ${response.statusText}`);
			}
		} catch (error) {
			console.error('Error resetting configuration:', error);
			this.showError('Failed to reset configuration: ' + error.message);

			// Fallback to local reset
			this.resetToLocalDefaults();
		} finally {
			this.setLoading(false);
		}
	}

	/**
	 * Reset to local defaults when server reset fails
	 */
	resetToLocalDefaults() {
		const advancedCheckbox = document.getElementById('advancedGpoEnabled');
		if (advancedCheckbox) {
			advancedCheckbox.checked = true;
			this.toggleAdvancedGpoSection(true);
		}

		this.setDefaultGpoValues();
		this.showSuccess('Configuration reset to local defaults');
	}

	/**
	 * Validate all GPO configurations
	 */
	validateAllConfigurations() {
		let isValid = true;

		for (let i = 1; i <= this.maxGpoPorts; i++) {
			if (!this.validateGpoConfiguration(i)) {
				isValid = false;
			}
		}

		return isValid;
	}

	/**
	 * Validate a specific GPO configuration
	 */
	validateGpoConfiguration(gpoNumber) {
		const controlSelect = document.getElementById(`gpo${gpoNumber}Control`);
		const stateSelect = document.getElementById(`gpo${gpoNumber}State`);

		if (!controlSelect) {
			return false;
		}

		const control = controlSelect.value;
		const state = stateSelect?.value;

		// Clear previous errors
		this.clearValidationErrors(gpoNumber);

		let isValid = true;

		// Validate state requirement for static and pulsed modes
		if ((control === 'static' || control === 'pulsed') && (!state || state === '')) {
			this.showValidationError(gpoNumber, 'state', 'State is required for this control mode');
			isValid = false;
		}

		// Validate pulse duration for pulsed mode
		if (control === 'pulsed') {
			if (!this.validatePulseDuration(gpoNumber)) {
				isValid = false;
			}
		}

		return isValid;
	}

	/**
	 * Validate pulse duration for a specific GPO
	 */
	validatePulseDuration(gpoNumber) {
		const pulseDurationInput = document.getElementById(`gpo${gpoNumber}PulseDuration`);
		const controlSelect = document.getElementById(`gpo${gpoNumber}Control`);

		if (!pulseDurationInput || !controlSelect) {
			return true; // Skip validation if elements not found
		}

		if (controlSelect.value !== 'pulsed') {
			return true; // No validation needed for non-pulsed modes
		}

		const duration = parseInt(pulseDurationInput.value);

		// Clear previous errors
		this.clearValidationError(gpoNumber, 'pulseDuration');

		if (isNaN(duration) || duration < 100 || duration > 60000) {
			this.showValidationError(gpoNumber, 'pulseDuration',
				'Pulse duration must be between 100 and 60000 milliseconds');
			return false;
		}

		return true;
	}

	/**
	 * Show validation error for a specific field
	 */
	showValidationError(gpoNumber, fieldType, message) {
		const fieldId = fieldType === 'state' ? `gpo${gpoNumber}State` : `gpo${gpoNumber}PulseDuration`;
		const field = document.getElementById(fieldId);

		if (field) {
			field.classList.add('error');

			// Create or update error message
			let errorDiv = document.getElementById(`${fieldId}Error`);
			if (!errorDiv) {
				errorDiv = document.createElement('div');
				errorDiv.id = `${fieldId}Error`;
				errorDiv.className = 'error';
				field.parentNode.appendChild(errorDiv);
			}
			errorDiv.textContent = message;
		}
	}

	/**
	 * Clear validation error for a specific field
	 */
	clearValidationError(gpoNumber, fieldType) {
		const fieldId = fieldType === 'state' ? `gpo${gpoNumber}State` : `gpo${gpoNumber}PulseDuration`;
		const field = document.getElementById(fieldId);
		const errorDiv = document.getElementById(`${fieldId}Error`);

		if (field) {
			field.classList.remove('error');
		}
		if (errorDiv) {
			errorDiv.remove();
		}
	}

	/**
	 * Clear all validation errors for a GPO
	 */
	clearValidationErrors(gpoNumber) {
		this.clearValidationError(gpoNumber, 'state');
		this.clearValidationError(gpoNumber, 'pulseDuration');
	}

	/**
	 * Map control mode from API format to UI format
	 */
	mapControlModeFromApi(apiControl) {
		const mapping = {
			'Static': 'static',
			'ReadingTags': 'reading-tags',
			'Pulsed': 'pulsed',
			'Network': 'network',
            'Running': 'running'
		};
		return mapping[apiControl] || 'static';
	}

	/**
	 * Map control mode from UI format to API format
	 */
	mapControlModeToApi(uiControl) {
		const mapping = {
			'static': 'Static',
			'reading-tags': 'ReadingTags',
			'pulsed': 'Pulsed',
			'network': 'Network',
			'running': 'Running'

		};
		return mapping[uiControl] || 'Static';
	}

	/**
	 * Map state from API format to UI format
	 */
	mapStateFromApi(apiState) {
		if (typeof apiState === 'string') {
			return apiState.toLowerCase();
		}
		return apiState === 1 || apiState === 'High' ? 'high' : 'low';
	}

	/**
	 * Map state from UI format to API format
	 */
	mapStateToApi(uiState) {
		return uiState === 'high' ? 'High' : 'Low';
	}

	/**
	 * Make API call with proper error handling
	 */
	async makeApiCall(method, endpoint, data = null) {
		const url = this.apiBaseUrl + endpoint;
		const options = {
			method: method,
			headers: {
				'Content-Type': 'application/json',
				'Accept': 'application/json'
			}
		};

		if (data) {
			options.body = JSON.stringify(data);
		}

		const response = await fetch(url, options);
		return response;
	}

	/**
	 * Set loading state
	 */
	setLoading(loading) {
		this.isLoading = loading;
		const loadingDiv = document.getElementById('loadingMessage');
		if (loadingDiv) {
			loadingDiv.style.display = loading ? 'block' : 'none';
		}

		// Disable form elements during loading
		const formElements = document.querySelectorAll('select, input, button');
		formElements.forEach(element => {
			element.disabled = loading;
		});
	}

	/**
	 * Show success message
	 */
	showSuccess(message) {
		const successDiv = document.getElementById('successMessage');
		if (successDiv) {
			successDiv.textContent = message;
			successDiv.style.display = 'block';
			setTimeout(() => {
				successDiv.style.display = 'none';
			}, 5000);
		}
	}

	/**
	 * Show error message
	 */
	showError(message) {
		console.error('GPO Configuration Error:', message);
		alert('Error: ' + message);
	}
}

// Global functions for backward compatibility
let gpoManager;

// Initialize when DOM is loaded
document.addEventListener('DOMContentLoaded', function () {
	gpoManager = new GpoConfigurationManager();
});

// Global functions called by HTML
function loadGpoConfiguration() {
	if (gpoManager) {
		return gpoManager.loadConfiguration();
	}
}

function saveGpoConfiguration() {
	if (gpoManager) {
		return gpoManager.saveConfiguration();
	}
}

function resetToDefaults() {
	if (gpoManager) {
		return gpoManager.resetToDefaults();
	}
}

function togglePulseDuration(gpoNumber) {
	if (gpoManager) {
		return gpoManager.togglePulseDuration(gpoNumber);
	}
}

var vueApplication = new Vue({
	el: '#configApp',

	data: {
		isDarkMode: false,
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
		adminCurrentPassword: "",
		adminNewPassword: "",
		adminNewPasswordCheck: "",
		rshellCurrentPassword: "",
		rshellNewPassword: "",
		rshellNewPasswordCheck: "",
		applicationLabel: 'version 4.0.1.13',
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
		sessionIsValid: true,
		clientCertFile: null,
		clientCertPassword: '',
		caCertFile: null,

	},


	ready: function () {
		// Load dark mode preference from localStorage
		const savedDarkMode = localStorage.getItem('darkMode');
		if (savedDarkMode !== null) {
			this.isDarkMode = JSON.parse(savedDarkMode);
			this.applyDarkMode();
		}

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
		containsWs() {
			return this.readerConfigs[0].mqttBrokerProtocol.includes('ws');
		},
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
		toggleDarkMode() {
			this.isDarkMode = !this.isDarkMode;
			this.applyDarkMode();
			// Save preference to localStorage
			localStorage.setItem('darkMode', JSON.stringify(this.isDarkMode));
		},

		applyDarkMode() {
			if (this.isDarkMode) {
				document.body.classList.add('dark-mode');
			} else {
				document.body.classList.remove('dark-mode');
			}
		},

		onClientCertSelected(event) {
			this.clientCertFile = event.target.files[0];
		},
		onCaCertSelected(event) {
			this.caCertFile = event.target.files[0];
		},

		async uploadClientCertificate() {
			if (!this.clientCertFile || !this.clientCertPassword) {
				alert('Please select a client certificate file and enter the password.');
				return;
			}

			let formData = new FormData();
			formData.append('file', this.clientCertFile);
			formData.append('password', this.clientCertPassword);

			// Basic Auth
			let basicAuth = btoa('admin:admin');
			try {
				let response = await fetch('/upload/mqtt/certificate', {
					method: 'POST',
					headers: {
						'Authorization': 'Basic ' + basicAuth
					},
					body: formData
				});

				if (response.ok) {
					alert('Client certificate uploaded successfully!');
				} else {
					let err = await response.text();
					alert('Failed to upload client certificate: ' + err);
				}
			} catch (error) {
				alert('Error uploading client certificate: ' + error);
			}
		},

		async uploadCaCertificate() {
			if (!this.caCertFile) {
				alert('Please select a CA certificate file.');
				return;
			}

			let formData = new FormData();
			formData.append('file', this.caCertFile);

			// Basic Auth
			let basicAuth = btoa('admin:admin');
			try {
				let response = await fetch('/upload/mqtt/ca', {
					method: 'POST',
					headers: {
						'Authorization': 'Basic ' + basicAuth
					},
					body: formData
				});

				if (response.ok) {
					alert('CA certificate uploaded successfully!');
				} else {
					let err = await response.text();
					alert('Failed to upload CA certificate: ' + err);
				}
			} catch (error) {
				alert('Error uploading CA certificate: ' + error);
			}
		},


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

		checkPassword: function (event) {
			if (this.adminNewPassword !== this.adminNewPasswordCheck) {
				document.getElementById('messageAdmin').style.color = 'red';
				document.getElementById('messageAdmin').innerHTML = 'The new passwords do not match.';
			}
			else {
				document.getElementById('messageAdmin').style.color = 'green';
				document.getElementById('messageAdmin').innerHTML = 'The new passwords match.';
				this.updateAdminPassword();
			}
			//if (document.getElementById('outputAdminNewPassword').value ==
			//	document.getElementById('outputAdminNewPasswordCheck').value) {
			//	document.getElementById('messageAdmin').style.color = 'green';
			//	document.getElementById('messageAdmin').innerHTML = 'Passwords matching';
			//	this.updateAdminPassword();
			//} else {
			//	document.getElementById('messageAdmin').style.color = 'red';
			//	document.getElementById('messageAdmin').innerHTML = 'Passwords not matching';
			//}
		},

		checkRShellPassword: function (event) {
			if (this.rshellNewPassword !== this.rshellNewPasswordCheck) {
				document.getElementById('messageRShell').style.color = 'red';
				document.getElementById('messageRShell').innerHTML = 'The new passwords do not match.';
			}
			else {
				document.getElementById('messageRShell').style.color = 'green';
				document.getElementById('messageRShell').innerHTML = 'The new passwords match.';
				this.updateRShellPassword();
			}
			//if (document.getElementById('outputRShellNewPassword').value ==
			//	document.getElementById('outputRShellNewPasswordCheck').value) {
			//	document.getElementById('messageRShell').style.color = 'green';
			//	document.getElementById('messageRShell').innerHTML = 'Passwords matching';
			//} else {
			//	document.getElementById('messageRShell').style.color = 'red';
			//	document.getElementById('messageRShell').innerHTML = 'Passwords not matching';
			//}
		},

		alertPasswordUpdated: function (event) {
			alert('Password updated, please stop and then reload the application.');
			this.adminCurrentPassword = "";
			this.adminNewPassword = "";
			this.adminNewPasswordCheck = "";
			this.rshellCurrentPassword = "";
			this.rshellNewPassword = "";
			this.rshellNewPasswordCheck = "";
		},

		alertPassworNotUpdated: function (event) {
			alert('Error changing password.');
			this.adminCurrentPassword = "";
			this.adminNewPassword = "";
			this.adminNewPasswordCheck = "";
			this.rshellCurrentPassword = "";
			this.rshellNewPassword = "";
			this.rshellNewPasswordCheck = "";
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

		restoreDefaultSettings: function () {
			
			const confirmed = window.confirm('Do you want to proceed with this action?');

			if (confirmed) {
				// User clicked OK (confirmed)
				// Perform the action or execute the code for confirmation
				console.log('reset to default settings confirmed.');
				//let encodedBasicData = btoa(this.adminAccount.username + ':' + this.adminAccount.password);
				var appReq = [];
				this.$http.get('/api/restore-default-settings')
					.success(function (appReq) {
						this.fetchReaderConfig();
					})
					.error(function (err) {
						console.log(err);
						this.alertSettingsNotApplied();
					});
			} else {
				// User clicked Cancel (not confirmed)
				// Handle the cancellation or do nothing
				console.log('reset to default settings canceled.');
			}			
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

		updateAdminPassword: function (event) {
			const eventDate = new Date().toISOString();
			var postData = {
				command: "update-admin-password",
				command_id: eventDate,
				payload: {
					username: "admin",
					currentPassword: this.adminCurrentPassword,
					newPassword: this.adminNewPassword
				}
			};
			// POST /apply-settings
			this.$http.post('/command/management', postData).then((response) => {

				// get status
				console.log(response.status);

				// get status text
				console.log(response.statusText);

				// get 'Expires' header
				//console.log(response.headers.get('Expires'));

				// set data on vm
				//this.$set('someData', response.body);
				console.log('Password updated!!');
				this.alertPasswordUpdated();


			}, (response) => {
				// error callback
				console.log('Error changing password:' + err);
				this.alertPassworNotUpdated();
			});
		},

		updateRShellPassword: function (event) {
			const eventDate = new Date().toISOString();
			var postData = {
				command: "update-rshell-password",
				command_id: eventDate,
				payload: {
					username: "root",
					currentPassword: this.rshellCurrentPassword,
					newPassword: this.rshellNewPassword
				}
			};
			// POST /apply-settings
			this.$http.post('/command/management', postData).then((response) => {

				// get status
				console.log(response.status);

				// get status text
				console.log(response.statusText);

				// get 'Expires' header
				//console.log(response.headers.get('Expires'));

				// set data on vm
				//this.$set('someData', response.body);
				console.log('Password updated!!');
				this.alertPasswordUpdated();


			}, (response) => {
				// error callback
				console.log('Error changing password:' + err);
				this.alertPassworNotUpdated();
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
			
			// Only encode URL fields to avoid breaking numeric/boolean values
			const urlFields = [
				'toiValidationUrl',
				'heartbeatUrl',
				'heartbeatHttpAuthenticationTokenApiUrl',
				'httpPostURL',
				'httpAuthenticationTokenApiUrl'
			];
			for (let keyName in this.readerConfigs[0]) {
				if (urlFields.includes(keyName) && this.readerConfigs[0][keyName]) {
					this.readerConfigs[0][keyName] = encodeURIComponent(this.readerConfigs[0][keyName]);
					console.log('Encoded URL field this.readerConfigs[0][' + keyName + ']: ' + this.readerConfigs[0][keyName]);
				}
			}

			// Validate advancedGpoMode values are within allowed range 0-11
			const gpoModes = [
				this.readerConfigs[0].advancedGpoMode1,
				this.readerConfigs[0].advancedGpoMode2,
				this.readerConfigs[0].advancedGpoMode3,
				this.readerConfigs[0].advancedGpoMode4
			];
			for (let i = 0; i < gpoModes.length; i++) {
				const mode = parseInt(gpoModes[i]);
				if (isNaN(mode) || mode < 0 || mode > 11) {
					alert('Invalid GPO mode value for port ' + (i + 1) + '. Must be between 0 and 11.');
					return;
				}
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
				console.log('Error applying settings:', response);
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
