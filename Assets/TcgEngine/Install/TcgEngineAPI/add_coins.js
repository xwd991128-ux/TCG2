const UserModel = require('./users/users.model');
const mongoose = require('mongoose');
const config = require('./config.js');

async function addCoins() {
    try {
        // Connect to MongoDB
        await mongoose.connect(config.database_url, {
            useNewUrlParser: true,
            useUnifiedTopology: true
        });
        console.log('Connected to MongoDB');

        // Find user by username
        const user = await UserModel.getByUsername('libai');
        if (!user) {
            console.log('User libai not found');
            return;
        }

        // Add coins
        const coinsToAdd = 50000;
        user.coins += coinsToAdd;
        console.log(`Adding ${coinsToAdd} coins to user ${user.username}`);
        console.log(`Old coins: ${user.coins - coinsToAdd}`);
        console.log(`New coins: ${user.coins}`);

        // Save changes
        const updatedUser = await UserModel.save(user, ['coins']);
        if (updatedUser) {
            console.log('Coins added successfully!');
        } else {
            console.log('Failed to add coins');
        }

        // Disconnect
        await mongoose.disconnect();
        console.log('Disconnected from MongoDB');
    } catch (error) {
        console.error('Error:', error);
        await mongoose.disconnect();
    }
}

addCoins();