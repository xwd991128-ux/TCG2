const mongoose = require('mongoose');
const CardModel = require('./cards/cards.model');
const config = require('./config.js');

async function checkCards() {
    try {
        // Connect to MongoDB
        await mongoose.connect(config.database_url, {
            useNewUrlParser: true,
            useUnifiedTopology: true
        });
        console.log('Connected to MongoDB');

        // Get all cards
        const cards = await CardModel.getAll();
        console.log(`Found ${cards.length} cards`);

        // Check rarity distribution
        const rarityCounts = {};
        cards.forEach(card => {
            const rarity = card.rarity || 'unknown';
            rarityCounts[rarity] = (rarityCounts[rarity] || 0) + 1;
        });

        console.log('Rarity distribution:');
        Object.entries(rarityCounts).forEach(([rarity, count]) => {
            console.log(`${rarity}: ${count} cards`);
        });

        // Check if there are any mythic cards
        const mythicCards = cards.filter(card => card.rarity === 'mythic');
        console.log(`Found ${mythicCards.length} mythic cards:`);
        mythicCards.forEach(card => {
            console.log(`- ${card.tid} (${card.rarity})`);
        });

        // Disconnect
        await mongoose.disconnect();
        console.log('Disconnected from MongoDB');
    } catch (error) {
        console.error('Error:', error);
        await mongoose.disconnect();
    }
}

checkCards();