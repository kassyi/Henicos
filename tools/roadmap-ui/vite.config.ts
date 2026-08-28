import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import fs from 'fs'
import path from 'path'
import { fileURLToPath } from 'url'

const __dirname = path.dirname(fileURLToPath(import.meta.url));

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [
    react(),
    {
      name: 'roadmap-api',
      configureServer(server) {
        server.middlewares.use((req, res, next) => {
          const dataPath = path.resolve(__dirname, '../../docs/data/roadmap_tasks.json');
          
          if (req.url === '/api/load' && req.method === 'GET') {
            try {
              if (fs.existsSync(dataPath)) {
                const data = fs.readFileSync(dataPath, 'utf-8');
                res.setHeader('Content-Type', 'application/json');
                res.end(data);
              } else {
                res.statusCode = 404;
                res.end(JSON.stringify({ error: 'Data file not found' }));
              }
            } catch (err) {
              res.statusCode = 500;
              res.end(JSON.stringify({ error: 'Failed to read data file' }));
            }
            return;
          }

          if (req.url === '/api/save' && req.method === 'POST') {
            let body = '';
            req.on('data', chunk => {
              body += chunk.toString();
            });
            req.on('end', () => {
              try {
                // Parse to validate JSON format
                const parsed = JSON.parse(body);
                fs.writeFileSync(dataPath, JSON.stringify(parsed, null, 2), 'utf-8');
                res.setHeader('Content-Type', 'application/json');
                res.end(JSON.stringify({ success: true }));
              } catch (err) {
                console.error("Save error:", err);
                res.statusCode = 500;
                res.end(JSON.stringify({ error: 'Failed to save data file' }));
              }
            });
            return;
          }

          next();
        });
      }
    }
  ],
})
