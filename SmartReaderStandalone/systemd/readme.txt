"System.Globalization.Invariant": true,

sudo apt-get install -y systemd

cp smartreader.service /etc/systemd/system

sudo cp smartreader /etc/systemd/system

cd /etc/systemd/system

sudo systemctl daemon-reload  
systemctl enable smartreader.service

sudo systemctl stop smartreader.service
sudo systemctl start smartreader.service  
sudo systemctl status smartreader.service
sudo journalctl --unit smartreader.service --follow


