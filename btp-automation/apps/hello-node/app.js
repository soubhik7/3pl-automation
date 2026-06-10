'use strict';
const express = require('express');
const app     = express();
const port    = process.env.PORT || 8080;

app.get('/', (req, res) => {
    res.json({
        app:    'hello-node',
        status: 'running',
        env:    process.env.APP_ENV || 'production',
    });
});

app.get('/health', (req, res) => {
    res.json({ status: 'ok' });
});

app.listen(port, () => {
    console.log(`hello-node listening on port ${port}`);
});
