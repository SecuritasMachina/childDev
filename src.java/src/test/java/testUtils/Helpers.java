package testUtils;

import java.lang.reflect.Type;
import java.util.Map;

import javax.naming.NamingException;

import org.apache.commons.lang3.RandomStringUtils;
import org.springframework.mock.jndi.SimpleNamingContextBuilder;

import com.ackdev.childDev.jdo.GoalItem;
import com.ackdev.childDev.jdo.GoalItemProgress;
import com.ackdev.childDev.jdo.JournalItem;
import com.common.security.dto.RegisterUserDTO;
import com.google.appengine.api.datastore.Text;
import com.google.common.collect.Maps;
import com.google.gson.Gson;
import com.google.gson.reflect.TypeToken;

public class Helpers {
	private static Gson gson = new Gson();

	public static void setupDS() {
		SimpleNamingContextBuilder builder = null;

	}

	public static void clearDS() {
		try {
			SimpleNamingContextBuilder.emptyActivatedContextBuilder();
		} catch (NamingException e) {
			// TODO Auto-generated catch block
			e.printStackTrace();
		}
	}

	public static String mapCompare(JournalItem eval2, JournalItem dataSourceModel) {
		String j1 = gson.toJson(eval2);
		String j2 = gson.toJson(dataSourceModel);
		Gson g = new Gson();
		Type mapType = new TypeToken<Map<String, Object>>() {
		}.getType();
		Map<String, Object> firstMap = g.fromJson(j1, mapType);
		Map<String, Object> secondMap = g.fromJson(j2, mapType);
		return Maps.difference(firstMap, secondMap).toString();
	}

	public static boolean objDBCompare(JournalItem eval2, JournalItem dataSourceModel) {
		String j1 = gson.toJson(eval2);
		String j2 = gson.toJson(dataSourceModel);

		return j1.equals(j2);
	}

	public static String mapCompare(GoalItem eval2, GoalItem dataSourceModel) {
		String j1 = gson.toJson(eval2);
		String j2 = gson.toJson(dataSourceModel);
		Gson g = new Gson();
		Type mapType = new TypeToken<Map<String, Object>>() {
		}.getType();
		Map<String, Object> firstMap = g.fromJson(j1, mapType);
		Map<String, Object> secondMap = g.fromJson(j2, mapType);
		return Maps.difference(firstMap, secondMap).toString();
	}

	public static boolean objDBCompare(GoalItem eval2, GoalItem dataSourceModel) {
		String j1 = gson.toJson(eval2);
		String j2 = gson.toJson(dataSourceModel);

		return j1.equals(j2);
	}

	public static String mapCompare(GoalItemProgress eval2, GoalItemProgress dataSourceModel) {
		String j1 = gson.toJson(eval2);
		String j2 = gson.toJson(dataSourceModel);
		Gson g = new Gson();
		Type mapType = new TypeToken<Map<String, Object>>() {
		}.getType();
		Map<String, Object> firstMap = g.fromJson(j1, mapType);
		Map<String, Object> secondMap = g.fromJson(j2, mapType);
		return Maps.difference(firstMap, secondMap).toString();
	}

	public static boolean objDBCompare(GoalItemProgress eval2, GoalItemProgress dataSourceModel) {
		String j1 = gson.toJson(eval2);
		String j2 = gson.toJson(dataSourceModel);

		return j1.equals(j2);
	}

	public static RegisterUserDTO makeUserReg() {
		RegisterUserDTO pRegisterUserDTO = new RegisterUserDTO();
		pRegisterUserDTO.appTag = "childDevTest";
		pRegisterUserDTO.password = RandomStringUtils.random(10, "1234567890abcfghjkpqrstuvwxz");
		pRegisterUserDTO.nickName = RandomStringUtils.random(10, "1234567890abcfghjkpqrstuvwxz");

		return pRegisterUserDTO;
	}

	public static JournalItem makeJournalItem() {
		JournalItem ret = new JournalItem();
		ret.accountFk = "1234567890abcfghjkpqrstuvwxz";
		ret.enteredDate = System.currentTimeMillis();
		ret.activity = RandomStringUtils.random(10, "1234567890abcfghjkpqrstuvwxz");
		ret.notes = new Text(RandomStringUtils.random(100, "1234567890abcfghjkpqrstuvwxz"));
		ret.tags = RandomStringUtils.random(10, "1234567890abcfghjkpqrstuvwxz") + " "
				+ RandomStringUtils.random(10, "1234567890abcfghjkpqrstuvwxz");
		return ret;
	}

	public static GoalItem makeGoalItem() {
		GoalItem ret = new GoalItem();
		ret.accountFk = "spoofAccountFK";
		ret.goalText = new Text(RandomStringUtils.random(100, "1234567890abcfghjkpqrstuvwxz"));
		ret.tags = RandomStringUtils.random(10, "1234567890abcfghjkpqrstuvwxz") + " "
				+ RandomStringUtils.random(10, "1234567890abcfghjkpqrstuvwxz");
		
		return ret;
	}
}
