

const columnDefs = [
    { field: 'barcode', headerName: 'ILPN', cellStyle: { fontSize: '20px' }, width: 300 },
    { field: 'sku', headerName: 'SKU', cellStyle: { fontSize: '20px' }, width: 300 },
    { field: 'qty', headerName: 'Qtde', cellStyle: { fontSize: '20px' }, width: 100 },
    //{ field: 'eventTimestamp', headerName: 'Timestamp', cellStyle: { fontSize: '20px' }, width: 100 },
];

var rowDataA = [];
var epcData = [];
var summaryEventData = [];

var clicks = 0;

var shouldRun = true;

//let btnGpo1 = document.querySelector('.gpo1btn');
var gpo1State = false;
var gpo2State = false;
var gpo3State = false;

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
    rowSelection: 'multiple',
    onSelectionChanged: () => {
        const selectedData = gridOptions.api.getSelectedRows();
        console.log('Selection Changed', selectedData);
    },
    //domLayout: 'autoHeight',
    onGridReady: (event) => {
        event.api.sizeColumnsToFit();

        window.addEventListener('resize', function () {
            setTimeout(function () {
                event.api.sizeColumnsToFit();
            })
        })

        init(event.api);
        
    }
};

function getSelectedRowData() {
    let selectedData = gridOptions.api.getSelectedRows();
    let summaryEventDataIndex = window.summaryEventData.findIndex(s => s.sku == selectedData[0].sku && s.barcode == selectedData[0].barcode && s.eventTimestamp == selectedData[0].eventTimestamp);
    if (summaryEventDataIndex >= 0) {
        let selectedSummaryData = window.summaryEventData[summaryEventDataIndex].epcs;
        let epcDisplayList = "";
        for (var i = 0; i < selectedSummaryData.length; i++) {
            epcDisplayList += selectedSummaryData[i] + "\n";
        }
        swal("\n\ILPN: " + selectedData[0].barcode, "Código: "+selectedData[0].sku +"\n\nContagem: " + selectedSummaryData.length + "\n\nEPCs:\n" + epcDisplayList);
        //prompt(`EPCs:\n`, epcDisplayList);
    }
    else {
        //alert(`Ordem: ${selectedData[0].sku}\nProduto: ${selectedData[0].barcode}\n`);
        swal("Ordem: " + selectedData[0].sku, "\n\nProduto: " + selectedData[0].barcode);
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
            shouldRun = true
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
            shouldRun = false
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
    this.rowDataA = [];
    this.epcData = [];
    this.summaryEventData = [];
    gridOptions.api.setRowData(this.rowDataA);
    gridOptions.api.sizeColumnsToFit();
    cleanTagCount();
}

function updateTagCount() {
    tagCount = this.rowDataA.length;
    document.getElementById("tagCount").innerHTML = tagCount;
};

function cleanTagCount() {
    tagCount = 0;
    document.getElementById("tagCount").innerHTML = tagCount;
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
                let skuSummaryEventsStringArray = cleanValue.split("],[");
                //console.log("skuSummaryEventsStringArray");
                //console.log(skuSummaryEventsStringArray);
                for (strIndex = 0; strIndex < skuSummaryEventsStringArray.length; strIndex++) {
                    let currentValue = skuSummaryEventsStringArray[strIndex] + "]";
                    //let skuSummaryEvents = JSON.parse(cleanValue);
                    if (!currentValue.endsWith("]")) {
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
                    
                    for (i = 0; i < skuSummaryEvents.length; i++) {

                        if (skuSummaryEvents[i].hasOwnProperty('sku')) {
                            let eventTimestamp = '';
                            let sku = 'UNKNOW';
                            let qty = '';
                            let barcode = '';

                            if (skuSummaryEvents[i].hasOwnProperty('eventTimestamp')) {
                                eventTimestamp = skuSummaryEvents[i].eventTimestamp;
                            }

                            if (skuSummaryEvents[i].hasOwnProperty('sku')) {
                                sku = skuSummaryEvents[i].sku;
                                console.log("sku: " + sku);
                            }

                            if (skuSummaryEvents[i].hasOwnProperty('qty')) {
                                qty = skuSummaryEvents[i].qty;
                            }

                            if (skuSummaryEvents[i].hasOwnProperty('barcode')) {
                                barcode = skuSummaryEvents[i].barcode;
                                if (barcode.length < 2 || barcode.indexOf("Read") > 0) {
                                    barcode = 'NoRead BC';
                                }
                                    
                            }

                            let skuEventRow = {
                                eventTimestamp: eventTimestamp,
                                sku: sku,
                                qty: qty,
                                barcode: barcode
                            };
                            if (!skuSummaryEvents[i].hasOwnProperty('epcs')) {
                                continue;
                            }


                            const skuIndex = this.rowDataA.findIndex(s => s.eventTimestamp == eventTimestamp && s.sku == sku);
                            //console.log("skuIndex: " + skuIndex);
                            if (shouldRun) {

                                if (skuIndex >= 0) {
                                    let currentSkuRow = this.rowDataA[skuIndex];
                                    currentSkuRow.qty = qty;
                                    if (currentSkuRow.eventTimestamp === 0) {
                                        currentSkuRow.eventTimestamp = eventTimestamp;
                                    }
                                    else {
                                        eventTimestamp = currentSkuRow.eventTimestamp;
                                    }
                                    this.rowDataA[skuIndex] = currentSkuRow;
                                    //console.log("updating sku qty: " + qty);
                                }
                                else {
                                    ///console.log("adding sku: " + sku);
                                    this.rowDataA.push(skuEventRow);
                                }
                                for (var epcIndex = 0; epcIndex < skuSummaryEvents[i].epcs.length; epcIndex++) {
                                    let containsEpc = this.epcData.some(element => {
                                        return element.toLowerCase() === skuSummaryEvents[i].epcs[epcIndex].toLowerCase();
                                    });
                                    if (!containsEpc) {
                                        this.epcData.push(skuSummaryEvents[i].epcs[epcIndex]);
                                        //let currentEpcToAdd = skuSummaryEvents[i].epcs[epcIndex];
                                        let currentEpcDataToAdd = {
                                            eventTimestamp: eventTimestamp,
                                            sku: sku,
                                            barcode: barcode,
                                            epcs: []
                                        }
                                        let summaryEventDataIndex = this.summaryEventData.findIndex(s => s.sku == sku && s.eventTimestamp == eventTimestamp);
                                        if (summaryEventDataIndex >= 0) {
                                            this.summaryEventData[summaryEventDataIndex].epcs.push(skuSummaryEvents[i].epcs[epcIndex]);
                                        }
                                        else {
                                            currentEpcDataToAdd.epcs.push(skuSummaryEvents[i].epcs[epcIndex]);
                                            this.summaryEventData.push(currentEpcDataToAdd);
                                        }
                                    }
                                }
                            }
                        }
                        else {
                            for (j = 0; j < skuSummaryEvents[i].length; j++) {
                                let eventTimestamp = '';
                                let sku = 'UNKNOW';
                                let qty = '';
                                let barcode = '';

                                if (skuSummaryEvents[i][j].hasOwnProperty('eventTimestamp')) {
                                    eventTimestamp = skuSummaryEvents[i][j].eventTimestamp;
                                }

                                if (skuSummaryEvents[i][j].hasOwnProperty('sku')) {
                                    sku = skuSummaryEvents[i][j].sku;
                                    
                                    //console.log("sku: " + sku);
                                }

                                if (skuSummaryEvents[i][j].hasOwnProperty('qty')) {
                                    qty = skuSummaryEvents[i][j].qty;
                                }

                                if (skuSummaryEvents[i][j].hasOwnProperty('barcode')) {
                                    barcode = skuSummaryEvents[i][j].barcode;
                                    if (barcode.length < 2 || barcode.indexOf("Read") > 0) {
                                        barcode = 'NoRead BC';
                                    }
                                }

                                let skuEventRow = {
                                    eventTimestamp: eventTimestamp,
                                    sku: sku,
                                    qty: qty,
                                    barcode: barcode
                                };

                                if (!skuSummaryEvents[i].hasOwnProperty('epcs')) {
                                    continue;
                                }
                                
                                const skuIndex = this.rowDataA.findIndex(s => s.eventTimestamp == eventTimestamp && s.sku == sku);
                                //console.log("skuIndex: " + skuIndex);
                                if (shouldRun) {

                                    if (skuIndex >= 0) {
                                        let currentSkuRow = this.rowDataA[skuIndex];
                                        currentSkuRow.qty = qty;
                                        if (currentSkuRow.eventTimestamp === 0) {
                                            currentSkuRow.eventTimestamp = eventTimestamp;
                                        }
                                        else {
                                            eventTimestamp = currentSkuRow.eventTimestamp;
                                        }
                                        this.rowDataA[skuIndex] = currentSkuRow;
                                        //console.log("updating sku qty: " + qty);
                                    }
                                    else {
                                       // console.log("adding sku: " + sku);
                                        this.rowDataA.push(skuEventRow);
                                    }

                                    for (var epcIndex = 0; epcIndex < skuSummaryEvents[i].epcs.length; epcIndex++) {
                                        let containsEpc = this.epcData.some(element => {
                                            return element.toLowerCase() === skuSummaryEvents[i].epcs[epcIndex].toLowerCase();
                                        });
                                        if (!containsEpc) {
                                            this.epcData.push(skuSummaryEvents[i].epcs[epcIndex]);
                                            //let currentEpcToAdd = skuSummaryEvents[i].epcs[epcIndex];
                                            let currentEpcDataToAdd = {
                                                eventTimestamp: eventTimestamp,
                                                sku: sku,
                                                barcode: barcode,
                                                epcs: []
                                            }
                                            let summaryEventDataIndex = this.summaryEventData.findIndex(s => s.sku == sku && s.eventTimestamp == eventTimestamp);
                                            if (summaryEventDataIndex >= 0) {
                                                this.summaryEventData[summaryEventDataIndex].epcs.push(skuSummaryEvents[i].epcs[epcIndex]);
                                            }
                                            else {
                                                currentEpcDataToAdd.epcs.push(skuSummaryEvents[i].epcs[epcIndex]);
                                                this.summaryEventData.push(currentEpcDataToAdd);
                                            }
                                        }
                                    }
                                }



                            }
                        }
                        
                        
                        if (shouldRun) {
                            gridOptions.api.setRowData(this.rowDataA);
                            console.log("OK. " + this.rowDataA.length);
                            gridOptions.api.sizeColumnsToFit();
                            updateTagCount();
                        }
                        
                    }

                }



            } catch (e) {
                console.log("ERROR. " + e);
            }
        }



    });
}

//init();