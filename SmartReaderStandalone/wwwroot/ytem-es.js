

const columnDefs = [
    { field: 'barcode', headerName: 'ID', cellStyle: { fontSize: '20px' }, width: 300 },
    { field: 'sku', headerName: 'Producto', cellStyle: { fontSize: '20px' }, width: 300 },
    { field: 'qty', headerName: 'Cantidad esperada', cellStyle: { fontSize: '20px' }, width: 100 },
    { field: 'read', headerName: 'Cantidad encontrada', cellStyle: { fontSize: '20px' }, width: 100 },
    { field: 'unexpected', headerName: 'Sobrante', cellStyle: { fontSize: '20px' }, width: 100 },
    //{ field: 'eventTimestamp', headerName: 'Timestamp', cellStyle: { fontSize: '20px' }, width: 100 },
];

var rowDataA = [];
var epcData = [];
var summaryEventData = [];
var tagCount = 0;
var itemCount = 0;

var clicks = 0;

var shouldRun = true;

var containsUnexpected = false;

//let btnGpo1 = document.querySelector('.gpo1btn');
var gpo1State = false;
var gpo2State = false;
var gpo3State = false;

var orderBarcode = "";
var contentFormat = "";
var transactionId = "";
var referenceListId = "";
var status = "";
var bizStep = "";
var bizLocation = "";

const gridOptions = {
    debounceVerticalScrollbar: true,
    alwaysShowVerticalScroll: true,
    defaultColDef: {
        sortable: true,
        filter: 'agTextColumnFilter',
        rowData: rowDataA,
        resizable: true
    },

    pagination: false,

    statusBar: {
        statusPanels: [
            {
                statusPanel: 'agTotalAndFilteredRowCountComponent',
                align: 'left',
            }
        ]
    },

    columnDefs: columnDefs,
    //domLayout: 'autoHeight',
    rowSelection: 'multiple',
    onSelectionChanged: () => {
        const selectedData = gridOptions.api.getSelectedRows();
        console.log('Selection Changed', selectedData);
    },

    onGridReady: (event) => {
        event.api.sizeColumnsToFit();

        window.addEventListener('resize', function () {
            setTimeout(function () {
                event.api.sizeColumnsToFit();
            })
        })

        init(event.api)
    }
};

function getSelectedRowData() {
    let selectedData = gridOptions.api.getSelectedRows();
    let summaryEventDataIndex = window.summaryEventData.findIndex(s => s.sku == selectedData[0].sku && s.eventTimestamp == selectedData[0].eventTimestamp);
    if (summaryEventDataIndex >= 0) {
        let selectedSummaryData = window.summaryEventData[summaryEventDataIndex].epcs;
        let epcDisplayList = "";
        for (var i = 0; i < selectedSummaryData.length; i++) {
            epcDisplayList += selectedSummaryData[i] + "\n";
        }
        fetch('/api/query/external/product/' + selectedData[0].sku)
            .then((response) => response.json())
            .then((data) => {
                console.log('requested remote data about ' + selectedData[0].sku);
                console.log('productLabelLong ' + data.productLabelLong);
                let productName = "";
                if (data.hasOwnProperty("productLabelLong")) {
                    productName = data.productLabelLong;
                }
                //alert(`Ordem: ${selectedData[0].sku}\n\nProduto: ${selectedData[0].barcode}\n${data.productLabelLong}\n\nContagem: ${selectedSummaryData.length}\n\nEPCs:\n${epcDisplayList}`);
                swal("ASN: " + selectedData[0].sku, "\n\nProducto: " + selectedData[0].barcode + "\n" + productName + "\n\nPuntaje: " + selectedSummaryData.length + "\n\nEPCs:\n" + epcDisplayList);
            })
            .catch(function (error) {
                console.log('There has been a problem with your fetch operation for product detail: ' + error.message);
                //alert(`Ordem: ${selectedData[0].sku}\nProduto: ${selectedData[0].barcode}\nContagem: ${selectedSummaryData.length}\nEPCs:\n${epcDisplayList}`);
                swal("ASN: " + selectedData[0].sku, "\n\nProducto: " + selectedData[0].barcode + "\n");
            });
        //prompt(`EPCs:\n`, epcDisplayList);
    }
    else {
        //alert(`Ordem: ${selectedData[0].sku}\nProduto: ${selectedData[0].barcode}\n`);
        swal("ASN: " + selectedData[0].sku, "\n\nProducto: " + selectedData[0].barcode);
    }
    return selectedData;
}

document
    .querySelector('#getSelectedRowData')
    .addEventListener('click', getSelectedRowData);

//document.addEventListener("DOMContentLoaded", function () {
//    var eGridDiv = document.querySelector('#data-table');
//    new agGrid.Grid(eGridDiv, gridOptions);
//    gridOptions.api.sizeColumnsToFit();
//});

const eGridDiv = document.getElementById('data-table');

new agGrid.Grid(eGridDiv, gridOptions);

function start() {
    fetch('/api/start-preset').then(function (response) {
        console.log('starting preset');
        if (response.ok) {
            console.log('request sent');
            //shouldRun = true
            document.getElementById("startBtn").className = "btn btn-success btn-lg";
            document.getElementById("stopBtn").className = "btn btn-primary btn-lg";
        } else {
            console.log('Network response was not ok.');
            alert("ERRO.");
        }
    })
        .catch(function (error) {
            console.log('There has been a problem with your fetch operation: ' + error.message);
            alert("ERRO inesperado. " + error.message);
        });

}

function stop() {
    fetch('/api/stop-preset').then(function (response) {
        console.log('starting preset');
        if (response.ok) {
            console.log('request sent');
            //shouldRun = false
            document.getElementById("stopBtn").className = "btn btn-danger btn-lg";
            document.getElementById("startBtn").className = "btn btn-primary btn-lg";
        } else {
            console.log('Network response was not ok.');
            alert("ERRO.");
        }
    })
        .catch(function (error) {
            console.log('There has been a problem with your fetch operation: ' + error.message);
            alert("ERRO inesperado. " + error.message);
        });

}


function cleanFilter() {
    fetch('/api/filter/clean').then(function (response) {
        console.log('cleaning-up filtered EPCs');
        if (response.ok) {
            console.log('request sent');
            alert("Solicitação enviada!");
        } else {
            console.log('Network response was not ok.');
            alert("ERRO.");
        }
    })
        .catch(function (error) {
            console.log('There has been a problem with your fetch operation: ' + error.message);
            alert("ERRO inesperado. " + error.message);
        });

}

function getApiKey() {
    fetch('/api/remote/key').then(function (response) {
        console.log('requesting remote api key');
        if (response.ok) {
            console.log('api key requested');
        } else {
            console.log('Network response was not ok.');
        }
    })
        .catch(function (error) {
            console.log('There has been a problem with your fetch operation: ' + error.message);
        });

}

function searchOrder() {

    this.rowDataA = [];
    this.epcData = [];
    this.summaryEventData = [];
    this.contentFormat = "";
    this.transactionId = "";
    this.status = "";
    this.bizStep = "";
    this.bizLocation = "";
    gridOptions.api.setRowData(this.rowDataA);
    gridOptions.api.sizeColumnsToFit();

    this.orderBarcode = document.getElementById("orderBarcode").value;

    var filter = {
        filters: [
            {
                property: "transactionId",
                operator: "EQ",
                values: [
                    this.orderBarcode
                ]
            },
            {
                property: "bizStep",
                operator: "EQ",
                values: [
                    "urn:epcglobal:cbv:bizstep:receiving"
                ]
            }
        ]
    };

    // request options

    var url = '/api/query/external/order';

    fetch(url, {
        credentials: 'include',
        method: 'POST',
        mode: 'cors',
        headers: {
            'Accept': '*/*',
            'Content-Type': 'application/json'
        },
        body: JSON.stringify(filter)
    }).then((response) => response.json())
        .then((data) => {
            console.log('data.length: ' + data.length);
            for (var i = 0; i < data.length; i++) {
                console.log('transactionId: ' + data[i].referenceList.transactionId);
                this.contentFormat = data[i].referenceList.contentFormat;
                this.transactionId = data[i].referenceList.transactionId;
                this.status = data[i].referenceList.status;
                console.log('referenceListId: ' + data[i].referenceList.referenceListId);
                this.referenceListId = data[i].referenceList.referenceListId;
                console.log('bizLocation: ' + data[i].referenceList.bizLocation);
                this.bizLocation = data[i].referenceList.bizLocation;
                console.log('bizStep: ' + data[i].referenceList.bizStep);
                this.bizStep = data[i].referenceList.bizStep;
                if (data[i].referenceList.containers.length > 0) {
                    //console.log('referenceList.containers.length: ' + data[i].referenceList.containers[j].length);
                    for (var j = 0; j < data[i].referenceList.containers.length; j++) {
                        console.log('content.length: ' + data[i].referenceList.containers[j].content.length);
                        if (data[i].referenceList.containers[j].content.length > 0) {
                            for (var k = 0; k < data[i].referenceList.containers[j].content.length; k++) {
                                console.log('gtin: ' + data[i].referenceList.containers[j].content[k].gtin);
                                console.log('quantity: ' + data[i].referenceList.containers[j].content[k].quantity);

                                let skuEventRow = {
                                    eventTimestamp: 0,
                                    read: 0,
                                    unexpected: 0,
                                    sku: data[i].referenceList.containers[j].content[k].gtin,
                                    qty: data[i].referenceList.containers[j].content[k].quantity,
                                    barcode: data[i].referenceList.referenceListId
                                };
                                this.rowDataA.push(skuEventRow);
                                gridOptions.api.setRowData(this.rowDataA);
                                console.log("OK. " + this.rowDataA.length);
                                gridOptions.api.sizeColumnsToFit();
                                updateTagCount();
                            }
                        }
                    }
                }
            }
        })
        .catch(function (error) {
            console.log('There has been a problem with your fetch operation: ' + error.message);
        });

}

function confirmData() {
    var proceed = true;
    this.orderBarcode = document.getElementById("orderBarcode").value;
    if (this.orderBarcode.length === 0 || this.contentFormat.length === 0
        || this.bizStep.length === 0 || this.bizLocation.length === 0) {
        proceed = false;
        swal("Enviar ASN ", "No se encontraron datos de ASN.", "error");
    }

    if (!proceed) {
        return;
    }

    if (this.containsUnexpected) {
        swal("Elementos inesperados identificados, ¿quieres enviarlos de todos modos?", {
            buttons: {
                cancel: "Não",
                send: {
                    text: "Enviar!",
                    value: "send",
                },
            },
        })
            .then((value) => {
                switch (value) {

                    case "cancel":
                        proceed = false;
                        break;

                    case "send":
                        proceed = true;
                        this.publishData();
                        break;

                    default:
                        proceed = false;
                        break;
                }
            });
    }
    else {
        publishData();
    }
}

function publishData() {

    var eventsArray = [];

    // var containerData = {
    //     contentFormat: this.contentFormat,
    //     referenceListId: this.referenceListId, 
    //     transactionId: this.transactionId,
    //     status: "done",
    //     bizStep: this.bizStep,
    //     bizLocation: this.bizLocation,
    //     // extensions: {
    //     //     "Extension_Test": "test"
    //     // },
    //     containers: [
    //         {
    //             content: [
    //                 //{
    //                 //    format: "tag",
    //                 //    hexa: ""
    //                 // }
    //             ]
    //         }
    //     ]
    // };
    const eventDate = new Date().toISOString();
    var containerData = {
        type: "ObjectEvent",
        eventTime: eventDate,
        disposition: "urn:epcglobal:cbv:disp:in_progress",
        readPoint: "R700",
        bizStep: this.bizStep,
        bizLocation: this.bizLocation,
        bizTransList: [
            {
                "value": this.transactionId
            }
        ],
        extension: {},
        epcList: [],
        action: "OBSERVE"
    };

    for (var i = 0; i < this.epcData.length; i++) {
        let contentData = {
            //format: "tag",
            hexa: this.epcData[i]
        }
        containerData.epcList.push(contentData);
    }
    eventsArray.push(containerData);
    dataToPublish = { events: eventsArray }
    var url = '/api/publish/external';
    console.log('dataToPublish: ' + JSON.stringify(dataToPublish));
    fetch(url, {
        credentials: 'include',
        method: 'POST',
        mode: 'cors',
        headers: {
            'Accept': '*/*',
            'Content-Type': 'application/json'
        },
        body: JSON.stringify(dataToPublish)
    }).then((response) => response.json())
        .then((data) => {
            console.log('data.length: ' + data.length);
            swal("ASN " + this.orderBarcode, "Datos enviados con éxito. ", "success");

        })
        .catch(function (error) {
            console.log('There has been a problem with your fetch operation: ' + error.message);
            swal("ASN " + this.orderBarcode, "Error en el envío de datos. " + error.message, "error");
        });

}

function gpo1() {
    if (!gpo1State) {
        fetch('/api/gpo/1/status/true').then(function (response) {
            console.log('setting GPO 1 to true');
            if (response.ok) {
                gpo1State = true;
                console.log('GPO 1 set to true');
                document.getElementById("gpo1btn").className = "btn btn-success btn-lg";
            } else {
                console.log('Network response was not ok.');
            }
        })
            .catch(function (error) {
                console.log('There has been a problem with your fetch operation: ' + error.message);
            });
    }
    else {
        console.log('setting GPO 1 to false');
        fetch('/api/gpo/1/status/false').then(function (response) {
            if (response.ok) {
                gpo1State = false;
                console.log('GPO 1 set to false');
                document.getElementById("gpo1btn").className = "btn btn-primary btn-lg";
            } else {
                console.log('Network response was not ok.');
            }
        })
            .catch(function (error) {
                console.log('There has been a problem with your fetch operation: ' + error.message);
            });
    }

}

function gpo2() {
    if (!gpo2State) {
        fetch('/api/gpo/2/status/true').then(function (response) {
            console.log('setting GPO 2 to true');
            if (response.ok) {
                gpo2State = true;
                console.log('GPO 2 set to true');
                document.getElementById("gpo2btn").className = "btn btn-success btn-lg";
            } else {
                console.log('Network response was not ok.');
            }
        })
            .catch(function (error) {
                console.log('There has been a problem with your fetch operation: ' + error.message);
            });
    }
    else {
        console.log('setting GPO 2 to false');
        fetch('/api/gpo/2/status/false').then(function (response) {
            if (response.ok) {
                gpo2State = false;
                console.log('GPO 2 set to false');
                document.getElementById("gpo2btn").className = "btn btn-primary btn-lg";
            } else {
                console.log('Network response was not ok.');
            }
        })
            .catch(function (error) {
                console.log('There has been a problem with your fetch operation: ' + error.message);
            });
    }

}

function gpo3() {
    if (!gpo3State) {
        fetch('/api/gpo/3/status/true').then(function (response) {
            console.log('setting GPO 3 to true');
            if (response.ok) {
                gpo3State = true;
                console.log('GPO 3 set to true');
                document.getElementById("gpo3btn").className = "btn btn-success btn-lg";
            } else {
                console.log('Network response was not ok.');
            }
        })
            .catch(function (error) {
                console.log('There has been a problem with your fetch operation: ' + error.message);
            });
    }
    else {
        console.log('setting GPO 3 to false');
        fetch('/api/gpo/3/status/false').then(function (response) {
            if (response.ok) {
                gpo3State = false;
                console.log('GPO 3 set to false');
                document.getElementById("gpo3btn").className = "btn btn-primary btn-lg";
            } else {
                console.log('Network response was not ok.');
            }
        })
            .catch(function (error) {
                console.log('There has been a problem with your fetch operation: ' + error.message);
            });
    }

}

function clean() {
    this.containsUnexpected = false;
    this.rowDataA = [];
    this.epcData = [];
    this.summaryEventData = [];
    this.orderBarcode = "";
    this.contentFormat = "";
    this.transactionId = "";
    this.status = "";
    this.bizStep = "";
    this.bizLocation = "";
    gridOptions.api.setRowData(this.rowDataA);
    gridOptions.api.sizeColumnsToFit();
    this.orderBarcode = "";
    document.getElementById("orderBarcode").value = orderBarcode;
    cleanTagCount();

}

function updateItemCount() {
    itemCount = window.epcData.length;
    document.getElementById("itemCount").innerHTML = itemCount;
};

function updateTagCount() {
    tagCount = window.rowDataA.length;
    document.getElementById("tagCount").innerHTML = tagCount;
};

function cleanTagCount() {
    tagCount = 0;
    itemCount = 0;
    document.getElementById("tagCount").innerHTML = tagCount;
    document.getElementById("itemCount").innerHTML = itemCount;
};

function init(api) {



    const input = document.querySelector('.input');
    const output = document.querySelector('.output');
    const close = document.querySelector('.close');
    const channel = Math.random();

    const supportsRequestStreams = (() => {
        let duplexAccessed = false;

        const hasContentType = new Request('', {
            body: new ReadableStream(),
            method: 'POST',
            get duplex() {
                duplexAccessed = true;
                return 'half';
            },
        }).headers.has('Content-Type');

        console.log({ duplexAccessed, hasContentType });

        return duplexAccessed && !hasContentType;
    })();

    if (!supportsRequestStreams) {
        output.textContent = `It doesn't look like your browser supports request streams.`;
        return;
    }

    //const stream = new ReadableStream({
    //    start(controller) {
    //        input.addEventListener('input', (event) => {
    //            event.preventDefault();
    //            controller.enqueue(input.value);
    //            input.value = '';
    //        });

    //        close.addEventListener('click', () => controller.close());
    //    }
    //}).pipeThrough(new TextEncoderStream());

    //fetch(`/send?channel=${channel}`, {
    //    method: 'POST',
    //    headers: { 'Content-Type': 'text/plain' },
    //    body: stream,
    //    duplex: 'half',
    //});

    fetch(`/api/stream/volumes`).then(async res => {
        const reader = res.body.pipeThrough(new TextDecoderStream()).getReader();
        while (true) {

            try {
                const { done, value } = await reader.read();
                if (done) return;
                //output.append(value);
                //console.log(value);
                let cleanValue = value.replace("[[", "[");
                cleanValue = cleanValue.replace("]]", "]");
                cleanValue = cleanValue.replace(",[", "[");
                if (cleanValue.startsWith(",")) {
                    cleanValue = cleanValue.substring(1);
                }
                //console.log(cleanValue);
                //],[
                //let skuSummaryEventsStringArray = cleanValue.split("],[");
                let skuSummaryEventsStringArray = JSON.parse(cleanValue);
                //console.log("skuSummaryEventsStringArray");
                //console.log(skuSummaryEventsStringArray);
                for (strIndex = 0; strIndex < skuSummaryEventsStringArray.length; strIndex++) {
                    let currentValue = skuSummaryEventsStringArray[strIndex];
                    //let currentValue = skuSummaryEventsStringArray[strIndex] + "]";
                    //let skuSummaryEvents = JSON.parse(cleanValue);
                    /*if (!currentValue.endsWith("]")) {
                        currentValue = currentValue.substring(0, currentValue.lastIndexOf("}"));
                        currentValue = currentValue + "}]";
                        currentValue = currentValue.replace("]]", "]");



                    }
                    if (currentValue.startsWith(",")) {
                        currentValue = currentValue.substring(1);
                    }

                    if (currentValue.startsWith("[{") && currentValue.endsWith("}]]")) {
                        currentValue = currentValue.substring(0, currentValue.length - 1);
                    }
                    //console.log("currentValue after clean-up");
                    //console.log(currentValue);
                    let skuSummaryEvents = JSON.parse(currentValue);
*/

                    let skuSummaryEvents = currentValue;
                    //for (i = 0; i < skuSummaryEvents.length; i++) {
                    //if(skuSummaryEvents)

                    if (currentValue.hasOwnProperty('sku')) {
                        let eventTimestamp = 0;
                        let sku = 'UNKNOW';
                        let qty = '';
                        let barcode = '';

                        if (currentValue.hasOwnProperty('eventTimestamp')) {
                            try {
                                eventTimestamp = currentValue.eventTimestamp;
                            } catch (error) {

                            }
                        }

                        if (currentValue.hasOwnProperty('sku')) {
                            sku = currentValue.sku;
                            console.log("sku: " + sku);
                        }

                        if (currentValue.hasOwnProperty('qty')) {
                            qty = currentValue.qty;
                        }

                        if (currentValue.hasOwnProperty('barcode')) {
                            barcode = currentValue.barcode;
                            if (barcode.length < 2 || barcode.indexOf("Read") > 0) {
                                barcode = 'NoRead BC';
                            }

                        }

                        let skuEventRow = {
                            eventTimestamp: eventTimestamp,
                            sku: sku,
                            qty: 0,
                            read: qty,
                            unexpected: qty,
                            barcode: barcode
                        };
                        if (!currentValue.hasOwnProperty('epcs')) {
                            continue;
                        }
                        const skuIndex = this.rowDataA.findIndex(s => s.sku == sku);
                        //console.log("skuIndex: " + skuIndex);
                        if (shouldRun) {

                            if (skuIndex >= 0) {

                                let currentSkuRow = this.rowDataA[skuIndex];
                                currentSkuRow.read = parseInt(currentSkuRow.read) + parseInt(qty);
                                if (parseInt(currentSkuRow.read) > parseInt(currentSkuRow.qty)) {
                                    currentSkuRow.unexpected = parseInt(currentSkuRow.read) - parseInt(currentSkuRow.qty);
                                    containsUnexpected = true;
                                }
                                if (currentSkuRow.eventTimestamp === 0) {
                                    try {
                                        currentSkuRow.eventTimestamp = eventTimestamp;
                                    } catch (error) {

                                    }

                                }
                                else {
                                    try {
                                        eventTimestamp = currentSkuRow.eventTimestamp;
                                    } catch (error) {

                                    }

                                }
                                this.rowDataA[skuIndex] = currentSkuRow;
                                //console.log("updating sku qty: " + qty);
                            }
                            else {
                                ///console.log("adding sku: " + sku);
                                this.rowDataA.push(skuEventRow);
                                containsUnexpected = true;
                            }

                            for (var epcIndex = 0; epcIndex < currentValue.epcs.length; epcIndex++) {
                                let containsEpc = this.epcData.some(element => {
                                    return element.toLowerCase() === currentValue.epcs[epcIndex].toLowerCase();
                                });
                                if (!containsEpc) {
                                    this.epcData.push(currentValue.epcs[epcIndex]);
                                    //let currentEpcToAdd = currentValue.epcs[epcIndex];
                                    let currentEpcDataToAdd = {
                                        eventTimestamp: eventTimestamp,
                                        sku: sku,
                                        barcode: barcode,
                                        epcs: []
                                    }
                                    let summaryEventDataIndex = -1;
                                    try {
                                        summaryEventDataIndex = this.summaryEventData.findIndex(s => s.sku == sku && s.eventTimestamp == this.rowDataA[skuIndex].eventTimestamp);
                                    } catch (error) {

                                    }

                                    if (summaryEventDataIndex >= 0) {
                                        this.summaryEventData[summaryEventDataIndex].epcs.push(currentValue.epcs[epcIndex]);
                                    }
                                    else {
                                        currentEpcDataToAdd.epcs.push(currentValue.epcs[epcIndex]);
                                        this.summaryEventData.push(currentEpcDataToAdd);
                                    }
                                }
                            }
                        }
                    }
                    // else {
                    //     for (j = 0; j < skuSummaryEvents[i].length; j++) {
                    //         let eventTimestamp = '';
                    //         let sku = 'UNKNOW';
                    //         let qty = '';
                    //         let barcode = '';

                    //         if (skuSummaryEvents[i][j].hasOwnProperty('eventTimestamp')) {
                    //             eventTimestamp = skuSummaryEvents[i][j].eventTimestamp;
                    //         }

                    //         if (skuSummaryEvents[i][j].hasOwnProperty('sku')) {
                    //             sku = skuSummaryEvents[i][j].sku;

                    //             //console.log("sku: " + sku);
                    //         }

                    //         if (skuSummaryEvents[i][j].hasOwnProperty('qty')) {
                    //             qty = skuSummaryEvents[i][j].qty;
                    //         }

                    //         if (skuSummaryEvents[i][j].hasOwnProperty('barcode')) {
                    //             barcode = skuSummaryEvents[i][j].barcode;
                    //             if (barcode.length < 2 || barcode.indexOf("Read") > 0) {
                    //                 barcode = 'NoRead BC';
                    //             }
                    //         }

                    //         let skuEventRow = {
                    //             eventTimestamp: eventTimestamp,
                    //             sku: sku,
                    //             qty: 0,
                    //             read: qty,
                    //             unexpected: qty,
                    //             barcode: barcode
                    //         };
                    //         if (!skuSummaryEvents[i].hasOwnProperty('epcs')) {
                    //             continue;
                    //         }

                    //         const skuIndex = this.rowDataA.findIndex(s => s.eventTimestamp == eventTimestamp && s.sku == sku);
                    //         //console.log("skuIndex: " + skuIndex);
                    //         if (shouldRun) {

                    //             if (skuIndex >= 0) {
                    //                 let currentSkuRow = this.rowDataA[skuIndex];
                    //                 currentSkuRow.qty = qty;
                    //                 if (currentSkuRow.eventTimestamp === 0) {
                    //                     currentSkuRow.eventTimestamp = eventTimestamp;
                    //                 }
                    //                 else {
                    //                     eventTimestamp = currentSkuRow.eventTimestamp;
                    //                 }
                    //                 this.rowDataA[skuIndex] = currentSkuRow;
                    //                 //console.log("updating sku qty: " + qty);
                    //             }
                    //             else {
                    //                 // console.log("adding sku: " + sku);
                    //                 this.rowDataA.push(skuEventRow);
                    //                 containsUnexpected = true;
                    //             }

                    //             for (var epcIndex = 0; epcIndex < skuSummaryEvents[i].epcs.length; epcIndex++) {
                    //                 let containsEpc = this.epcData.some(element => {
                    //                     return element.toLowerCase() === skuSummaryEvents[i].epcs[epcIndex].toLowerCase();
                    //                 });
                    //                 if (!containsEpc) {
                    //                     this.epcData.push(skuSummaryEvents[i].epcs[epcIndex]);
                    //                     //let currentEpcToAdd = skuSummaryEvents[i].epcs[epcIndex];
                    //                     let currentEpcDataToAdd = {
                    //                         eventTimestamp: eventTimestamp,
                    //                         sku: sku,
                    //                         barcode: barcode,
                    //                         epcs: []
                    //                     }
                    //                     let summaryEventDataIndex = this.summaryEventData.findIndex(s => s.sku == sku && s.eventTimestamp == eventTimestamp);
                    //                     if (summaryEventDataIndex >= 0) {
                    //                         this.summaryEventData[summaryEventDataIndex].epcs.push(skuSummaryEvents[i].epcs[epcIndex]);
                    //                     }
                    //                     else {
                    //                         currentEpcDataToAdd.epcs.push(skuSummaryEvents[i].epcs[epcIndex]);
                    //                         this.summaryEventData.push(currentEpcDataToAdd);
                    //                     }
                    //                 }
                    //             }
                    //         }


                    //     }
                    // }
                }

                if (shouldRun) {
                    gridOptions.api.setRowData(this.rowDataA);
                    console.log("OK. " + this.rowDataA.length);
                    gridOptions.api.sizeColumnsToFit();
                    updateTagCount();
                    updateItemCount();
                }

                //}





            } catch (e) {
                console.log("ERROR. " + e);
            }
        }



    });
}

//init();