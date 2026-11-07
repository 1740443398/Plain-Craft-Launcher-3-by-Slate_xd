const http = require('http');
const fs = require('fs');
const path = require('path');
const ROOT = process.argv[2];
const PORT = parseInt(process.argv[3], 10);
http.createServer((req, res) => {
  const name = req.url.replace(/^\//, '').split('?')[0];
  const file = path.join(ROOT, name);
  if (!fs.existsSync(file)) { res.writeHead(404); res.end('nope'); return; }
  const stat = fs.statSync(file);
  res.writeHead(200, { 'Content-Length': stat.size, 'Content-Type': 'application/octet-stream' });
  fs.createReadStream(file).pipe(res);
}).listen(PORT, '127.0.0.1', () => console.log('listening ' + PORT));