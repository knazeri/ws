import ws from 'k6/ws';
import { check } from 'k6';

export let options = {
    stages: [
        { duration: '30s', target: 5000 }, // ramp up to 5000 connections in 30 seconds
        { duration: '30s', target: 5000 }, // stay at 5000 connections for 30 seconds
        { duration: '30s', target: 0 }, // ramp down to 0 connections in 30 seconds
    ],
};

export default function () {
    const roomname = 'testroom';
    const nickname = `user${__VU}_${__ITER}`;
    const url = `wss://localhost:54866/chat/${roomname}?nickname=${nickname}`;

    const res = ws.connect(url, {}, function (socket) {
        socket.on('open', () => console.log('connected'));
        socket.on('message', (data) => console.log('Message received: ', data));
        socket.on('close', () => console.log('disconnected'));
    });

    check(res, { 'status is 101': (r) => r && r.status === 101 });
}