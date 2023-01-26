var mqtt = require('mqtt')
var fs = require('fs');

function convertKeyValuePairToJSON(kvpData) {
	var jsonObject;
	var jsonString = "{ ";
	var lines = kvpData.toString().split("\n");
	var hasPreviousElement = false;
	var lineCounter = 0;

	lines.forEach(function(line) {
		if (line != null) {
			var trimmed_line = line.trim();
			if (line.length > 0 && line[0] != '\/') {
				var keyvalpair = line.toString().split("=");
				if (keyvalpair.length != 2) {
					return; // Break out of the inner function
				}
				if (hasPreviousElement && lineCounter < lines.length) {
					jsonString += ',';
				}
				jsonString += '"' + keyvalpair[0].toString().trim() + '" : "'
						+ keyvalpair[1].toString().trim() + '"';
				hasPreviousElement = true;
				lineCounter = lineCounter + 1;
				// console.log("Config Json Item: " +
				// '"'+keyvalpair[0].toString().trim()+'" :
				// "'+keyvalpair[1].toString().trim()+'"');

			}
		}
	});
	jsonString += " }";
	// jsonObject = JSON.parse(jsonString);
	console.log("Config Json: " + jsonString);
	return jsonString;

}

var configFileData = fs.readFileSync("/cust/config/smartreader.cfg");

var json = JSON.parse(convertKeyValuePairToJSON(configFileData));
if (json.hasOwnProperty('mqttEnabled') && json['mqttEnabled'] == '1') {
	var mqttClientId = json['readerName'].toString().replace('"', '');
	mqttClientId = mqttClientId.toString().replace('"', '') + '-listener';
	var mqttBrokerAddress = json['mqttBrokerAddress'];
	var mqttTopic = json['mqttTopic'].toString()
			.replace('inventory', 'command');

	var client = mqtt.connect(mqttBrokerAddress, {
		clientId : mqttClientId
	});

	client.on('connect', function() {
		client.subscribe(mqttTopic.toString());
		// client.publish('presence', 'Hello mqtt')
	})

	client.on('message', function(topic, message) {
		try {
			// message is Buffer
			console.log(message.toString());
			if (message.toString().trim() == 'START') {
				console.log('START requested');
				fs.unlinkSync('/cust/pause-request');
			} else if (message.toString().trim() == 'STOP') {
				console.log('STOP requested');
				fs.writeFile('/cust/pause-request', '1', function(err) {
					if (err) {
						console.log(err);
					}
					console.log('file created: /cust/pause-request');
				});

			} else {
				console.log('Unknow message');
			}
		} catch (ex) {
			console.log('ERROR: ' + ex.message);
		}

		// client.end()
	})
} else {
	console.log('ERROR MQTT NOT DETECTED. ');
}
